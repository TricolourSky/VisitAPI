using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Quests;
using HarmonyLib;

namespace VisitAPI.Native;

/// <summary>章节自动接取链（1.1 的 AutoStart 在 0.16 的替身）：
/// ① 子任务标 `visitapi.autoStart` → 一变"可接"就自动接，**但要等所属章节先开始**（ChapterOpen）；标 `autoFinish` → 一达成就自动交；
/// ② 章节任务本身没人会去点：任一子任务开始就自动接章节、子任务全完成（引擎判可交）就自动交章节——邮件/奖励照原生走。
/// 挂在 QuestController 两个入口：状态变化事件 + 任务进书（含登录整本扫）。
/// 实际接/交走 Core\QuestOps 唯一出口（T-6）：等一帧、状态复查、与触发器共用在途去重——双交任务从根上杜绝。
/// 顺带担任 G7 的信号源：每次过手都点一下 ChapterEvents。DEV_NOTES #71。</summary>
public static class ChapterChain
{
    /// 最近一个在用的任务控制器（flags 迟到时 Rescan 拿它补跑；章节横幅取章节名也用）
    public static QuestController Controller;

    [HarmonyPatch(typeof(QuestController), nameof(QuestController.OnConditionalStatusChangedEvent))]
    public static class Changed { static void Postfix(QuestController __instance, Quest conditional) => Check(__instance, conditional); }

    [HarmonyPatch(typeof(QuestController), nameof(QuestController.ManageConditional))]
    public static class Added { static void Postfix(QuestController __instance, Quest conditional) => Check(__instance, conditional); }

    // 阶段四单点隔离：Check 挂在任务状态事件的两个入口上，自己炸会打断引擎的事件派发
    static void Check(QuestController qc, Quest quest)
    {
        try { CheckCore(qc, quest); }
        catch (System.Exception e) { Plugin.Log.LogError("[chain] 自动链检查失败（任务事件本体不受影响）: " + e); }
    }

    static void CheckCore(QuestController qc, Quest quest)
    {
        if (quest?.Template == null || qc?.Quests == null) return;
        // 坑 #98 同病（09-07 终审）：藏身处 3D 里玩家挂的是 QuestControllerClientLocalGame，它的 Accept/Finish 只改内存、本地发奖励、不进档案；
        // 触发器早改走 QuestOps.Resolve() 拿 Backend 版，自动链却一直用事件的 __instance。目标在藏身处达成时会「本地交一次、出去再交一次」。
        // 事件来自 LocalGame 版 + 当前世界是藏身处 → 换成 Backend 版并按 id 重取任务；换不到就跳过（登录整本扫 / 战局内不受影响）。
        if (qc is QuestControllerClientLocalGame && Singleton<GameWorld>.Instantiated && Raid.IsHideout(Singleton<GameWorld>.Instance.LocationId))
        {
            var backend = QuestOps.Resolve();
            if (backend == null || backend is QuestControllerClientLocalGame || backend.Quests == null) { Plugin.Log.LogDebug("[chain] 藏身处事件来自 LocalGame 控制器且拿不到 Backend 版，跳过"); return; }
            var real = backend.Quests.GetConditional(quest.Id);
            if (real == null) return;
            qc = backend; quest = real;
        }
        Controller = qc; QuestFlags.MarkStory(quest);
        if (QuestFlags.IsStory(quest.Id)) ChapterEvents.Raise();   // 只有剧情家族才值得章节屏重画一次
        var st = quest.QuestStatus;
        if (AutoReady(qc, quest.Id) && st == EQuestStatus.AvailableForStart && ChapterOpen(qc, quest.Id)) QuestOps.Accept(qc, quest, "chain");
        if ((QuestFlags.IsChapter(quest.Id) || QuestFlags.AutoFinish(quest.Id)) && st == EQuestStatus.AvailableForFinish) QuestOps.Finish(qc, quest, "chain");
        // 章节跟着子任务走：任一子任务开始就接章节。子任务先到（状态变化）或章节先到（登录整本扫）都接得住
        var chapterId = QuestFlags.IsChapter(quest.Id) ? quest.Id : QuestFlags.ChapterOf(quest.Id);
        var chapter = chapterId == null ? null : chapterId == quest.Id ? quest : qc.Quests.GetConditional(chapterId);
        if (chapter != null && chapter.QuestStatus == EQuestStatus.AvailableForStart
            && QuestFlags.SubsOf(chapterId).Any(id => qc.Quests.GetConditional(id)?.QuestStatus >= EQuestStatus.Started)) QuestOps.Accept(qc, chapter, "chain");
        // 章节刚开门：把卡在 ChapterOpen 上等开门的 autoStart 子任务放出来（它们自己不会再收到状态变化事件，得由章节主动点名）
        if (chapter != null && chapter.QuestStatus >= EQuestStatus.Started)
            foreach (var id in QuestFlags.SubsOf(chapterId))
            {
                var sub = qc.Quests.GetConditional(id);
                if (sub != null && AutoReady(qc, id) && sub.QuestStatus == EQuestStatus.AvailableForStart) QuestOps.Accept(qc, sub, "chain");
            }
        // startAfter 是跨任务前置：前置完成时点名等它的任务（可能在别的章节，上面按本章节扫的循环够不着；09-07 终审）
        if (st == EQuestStatus.Success)
            foreach (var id in QuestFlags.WaitingOn(quest.Id))
            {
                var waiting = qc.Quests.GetConditional(id);
                if (waiting != null && waiting.QuestStatus == EQuestStatus.AvailableForStart && ChapterOpen(qc, id)) QuestOps.Accept(qc, waiting, "chain");
            }
    }

    /// <summary>子任务的「自动接」是章节内部的接力棒：所属章节没开始就先别发。
    /// 不设这道闸，一条没有前置的子任务会在登录、任务书刚建好那一刻就被接下——人还在菜单里横幅就弹了（实机踩过）。
    /// 想让整章自动开始，把 autoStart 标在**章节**上：章节自己不属于任何章节，不受这道闸。</summary>
    static bool ChapterOpen(QuestController qc, string questId)
    {
        var chapterId = QuestFlags.ChapterOf(questId);
        if (chapterId == null) return true;                       // 不是谁的子任务：老行为不变
        var chapter = qc.Quests.GetConditional(chapterId);
        return chapter != null && chapter.QuestStatus >= EQuestStatus.Started;
    }

    /// <summary>这条任务够不够格「自动接」。标了 startAfter 就等指定任务 == Success —— VisitAPI 自己的前置，战局内也算数。
    /// 两者都写时 startAfter 更严、它说了算。判定必须 == Success：>= 会把失败家族一起放行。</summary>
    static bool AutoReady(QuestController qc, string questId)
    {
        var after = QuestFlags.StartAfter(questId);
        if (after == null) return QuestFlags.AutoStart(questId);
        return qc.Quests.GetConditional(after)?.QuestStatus == EQuestStatus.Success;
    }

    /// <summary>flags 迟到时补跑一轮：先把章节表登记齐，再让每条任务重过一遍自动链判定。
    /// 登录那轮 ManageConditional 扫描在 flags 没到时是空跑的（IsChapter 全 false → 点名循环压根不进）。</summary>
    public static void Rescan()
    {
        var qc = Controller; if (qc?.Quests == null) return;
        var book = qc.Quests.ToList();
        foreach (var q in book) QuestFlags.MarkStory(q);
        foreach (var q in book) Check(qc, q);
    }
}
