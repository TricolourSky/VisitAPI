using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EFT.Quests;
using HarmonyLib;

namespace VisitAPI.Native;

/// <summary>章节自动接取链（1.1 的 AutoStart 在 0.16 的替身）：
/// ① 子任务 JSON 标 `visitapi.autoStart` → 它一变成"可接"就自动接下，**但要等所属章节先开始**（见 ChapterOpen）；标 `visitapi.autoFinish` → 一达成就自动交（1.1 的剧情任务就是不用交的）；
/// ② 章节任务本身没人会去点：任一子任务开始就自动接章节、子任务全部完成（引擎判成可交）就自动交章节——章节的邮件/奖励照原生走。
/// 挂在 QuestController 的两个入口：状态变化事件 + 任务进书（含登录时整本扫一遍）。动作推迟一帧，不在引擎的事件派发里再改状态。DEV_NOTES #71。</summary>
public static class ChapterChain
{
    static readonly HashSet<string> _busy = new();
    /// 最近一个在用的任务控制器（flags 迟到时 QuestFlags 拿它补登记章节）
    public static QuestController Controller;

    [HarmonyPatch(typeof(QuestController), nameof(QuestController.OnConditionalStatusChangedEvent))]
    static class Changed { static void Postfix(QuestController __instance, Quest conditional) => Check(__instance, conditional); }

    [HarmonyPatch(typeof(QuestController), nameof(QuestController.ManageConditional))]
    static class Added { static void Postfix(QuestController __instance, Quest conditional) => Check(__instance, conditional); }

    static void Check(QuestController qc, Quest quest)
    {
        if (quest?.Template == null || qc?.Quests == null) return;
        Controller = qc; QuestFlags.MarkStory(quest);
        var st = quest.QuestStatus;
        if (QuestFlags.AutoStart(quest.Id) && st == EQuestStatus.AvailableForStart && ChapterOpen(qc, quest.Id)) Run(qc, quest, "accept");
        if ((QuestFlags.IsChapter(quest.Id) || QuestFlags.AutoFinish(quest.Id)) && st == EQuestStatus.AvailableForFinish) Run(qc, quest, "finish");
        // 章节跟着子任务走：任一子任务开始就接章节。子任务先到（状态变化）或章节先到（登录整本扫描）都得接得住
        var chapterId = QuestFlags.IsChapter(quest.Id) ? quest.Id : QuestFlags.ChapterOf(quest.Id);
        var chapter = chapterId == null ? null : chapterId == quest.Id ? quest : qc.Quests.GetConditional(chapterId);
        if (chapter != null && chapter.QuestStatus == EQuestStatus.AvailableForStart
            && QuestFlags.SubsOf(chapterId).Any(id => qc.Quests.GetConditional(id)?.QuestStatus >= EQuestStatus.Started)) Run(qc, chapter, "accept");
        // 章节刚开门：把那些卡在 ChapterOpen 上、等着开门的 autoStart 子任务放出来
        // （它们自己不会再收到状态变化事件了，得由章节这一头主动去点名）
        if (chapter != null && chapter.QuestStatus >= EQuestStatus.Started)
            foreach (var id in QuestFlags.SubsOf(chapterId))
            {
                var sub = qc.Quests.GetConditional(id);
                if (sub != null && QuestFlags.AutoStart(id) && sub.QuestStatus == EQuestStatus.AvailableForStart) Run(qc, sub, "accept");
            }
    }

    /// <summary>子任务的「自动接」是**章节内部的接力棒**：所属章节还没开始，就先别发。
    /// <para>不设这道闸的话，一条没有前置的子任务会在**登录、任务书刚建好的那一刻**就被接下 ——
    /// 玩家人还在菜单里，任务横幅和章节横幅就已经弹出来了（实机踩过）。
    /// 想让整章自动开始，把 <c>autoStart</c> 标在**章节**上：章节自己不属于任何章节，不受这道闸。</para></summary>
    static bool ChapterOpen(QuestController qc, string questId)
    {
        var chapterId = QuestFlags.ChapterOf(questId);
        if (chapterId == null) return true;                       // 不是谁的子任务：老行为不变
        var chapter = qc.Quests.GetConditional(chapterId);
        return chapter != null && chapter.QuestStatus >= EQuestStatus.Started;
    }

    static void Run(QuestController qc, Quest quest, string what)
    {
        if (_busy.Add(quest.Id)) Plugin.Instance.StartCoroutine(Later(qc, quest, what));
    }

    static IEnumerator Later(QuestController qc, Quest quest, string what)
    {
        yield return null;
        var accept = what == "accept";
        Task task = quest.QuestStatus != (accept ? EQuestStatus.AvailableForStart : EQuestStatus.AvailableForFinish) ? null
            : accept ? qc.AcceptQuest(quest, runNetworkTransaction: true) : qc.FinishQuest(quest, runNetworkTransaction: true);
        while (task != null && !task.IsCompleted) yield return null;
        if (task != null && task.IsFaulted) Plugin.Log.LogWarning($"[chain] {what} {quest.Id} failed: {task.Exception?.GetBaseException().Message}");
        else if (task != null) Plugin.Log.LogDebug($"[chain] {what} {quest.Id} -> {quest.QuestStatus}");
        _busy.Remove(quest.Id);
    }
}
