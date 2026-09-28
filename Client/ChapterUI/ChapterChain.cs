using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Quests;
using HarmonyLib;

namespace VisitAPI.Native;

public static class ChapterChain
{
    public static QuestController Controller;

    [HarmonyPatch(typeof(QuestController), nameof(QuestController.OnConditionalStatusChangedEvent))]
    public static class Changed { static void Postfix(QuestController __instance, Quest conditional) => Check(__instance, conditional, live: true); }

    [HarmonyPatch(typeof(QuestController), nameof(QuestController.ManageConditional))]
    public static class Added
    {
        static void Prefix(QuestController __instance, Quest conditional)
        {
            if (VisitGameGuard.IsVisitController(__instance)) return;
            LocalFail.Record(conditional);   // 09-24：进任务书前记下服务端给的状态（见 LocalFail）
        }
        static void Postfix(QuestController __instance, Quest conditional) => Check(__instance, conditional, live: false);
    }

    /// live = 本会话亲眼看到的状态迁移（OnConditionalStatusChangedEvent）；false = 任务刚进任务书 / 重扫时的初始状态
    static void Check(QuestController qc, Quest quest, bool live)
    {
        // 09-24 审查 H2：访问商人时引擎的 NarrateGame.Create 另建一个任务控制器并 Run，它对每条任务调 ManageConditional，
        // 以前会把全局 Controller 换成这个访问结束就丢的副本（邀请信、金色电话、地图锁之后都读它）。这里整个跳过
        if (VisitGameGuard.IsVisitController(qc)) return;
        try { CheckCore(qc, quest, live); }
        catch (System.Exception e) { Plugin.Log.LogError("[chain] Auto-chain check failed (the quest event itself is unaffected): " + e); }
    }

    static void CheckCore(QuestController qc, Quest quest, bool live)
    {
        if (quest?.Template == null || qc?.Quests == null) return;
        if (qc is QuestControllerClientLocalGame && Singleton<GameWorld>.Instantiated && Raid.IsHideout(Singleton<GameWorld>.Instance.LocationId))
        {
            var backend = QuestOps.Resolve();
            if (backend == null || backend is QuestControllerClientLocalGame || backend.Quests == null) return;
            var real = backend.Quests.GetConditional(quest.Id);
            if (real == null) return;
            qc = backend; quest = real;
        }
        // 09-26 F2：撤离瞬间（战局控制器已在 CleanUp 里释放）、结算页、大厅重建途中的事件一律不接手——不改全局 Controller、不自动接 / 交、不发请求；
        // 回到大厅后 QuestOps.OnLobby 会对大厅那份整本重扫一遍
        if (!QuestOps.IsLive(qc)) return;
        Controller = qc; QuestFlags.MarkStory(quest);
        ChapterUI.SkipState.Use(qc.Profile?.Id);
        if (QuestFlags.IsStory(quest.Id)) { ChapterEvents.Raise(); ChapterUI.SkipState.Note(quest); }
        QuestConditionNotify.Seed(quest);
        var st = quest.QuestStatus;
        LocalFail.Check(qc, quest);   // 09-24：大厅里按全局变量失败的剧情任务，引擎不上报服务端，替它发 QuestFail
        QuestVars.Apply(quest, live); // 09-24：任务进入 Started / Success / Fail 时按 visitapi.setVariables 给档案变量赋值（1.1 的 GlobalVariable 奖励）
        if (AutoReady(qc, quest.Id) && st == EQuestStatus.AvailableForStart && ChapterOpen(qc, quest.Id)) AutoAccept(qc, quest);
        if ((QuestFlags.IsChapter(quest.Id) || QuestFlags.AutoFinish(quest.Id)) && st == EQuestStatus.AvailableForFinish) QuestOps.Finish(qc, quest, "chain");
        var chapterId = QuestFlags.IsChapter(quest.Id) ? quest.Id : QuestFlags.ChapterOf(quest.Id);
        var chapter = chapterId == null ? null : chapterId == quest.Id ? quest : qc.Quests.GetConditional(chapterId);
        // 章节里任一子任务已动起来 → 章节开门；写在章节 startAfter 里的「起点」不算（它们本来就在开门前跑，比如踩坠机那条）
        if (chapter != null && chapter.QuestStatus == EQuestStatus.AvailableForStart
            && QuestFlags.SubsOf(chapterId).Any(id => !QuestFlags.IsStarterOf(chapterId, id) && ChapterUI.ChapterStates.Begun(qc.Quests.GetConditional(id)?.QuestStatus ?? EQuestStatus.Locked))) AutoAccept(qc, chapter);
        if (chapter != null && ChapterUI.ChapterStates.Begun(chapter.QuestStatus))
            foreach (var id in QuestFlags.SubsOf(chapterId))
            {
                var sub = qc.Quests.GetConditional(id);
                if (sub != null && AutoReady(qc, id) && sub.QuestStatus == EQuestStatus.AvailableForStart) AutoAccept(qc, sub);
            }
        if (st == EQuestStatus.Success)
            foreach (var id in QuestFlags.WaitingOn(quest.Id))
            {
                var waiting = qc.Quests.GetConditional(id);
                if (waiting != null && waiting.QuestStatus == EQuestStatus.AvailableForStart && ChapterOpen(qc, id)) AutoAccept(qc, waiting);
            }
    }

    static void AutoAccept(QuestController qc, Quest quest)
    {
        if (!PrereqsMet(qc, quest)) return;
        // 09-24 14:14 曾在对话屏开着时把自动接攒到对话结束（SORA 觉得同一场对话顺进下一条任务「乱」），14:40 撤了：
        // 1.1 的对话树本来就是「我会联系你的」之后跳回枢纽重新评估、当场给下一个话题（SPT5 服务端也是接下即自动开章节里的任务）；
        // 攒着不接反而让枢纽没话可说——退出线的条件不满足、任务清单又是空的，只剩一个红色「返回」来回死循环（SORA 14:35 卡住那次）
        QuestOps.Accept(qc, quest, "chain");
    }

    static bool PrereqsMet(QuestController qc, Quest quest)
    {
        if (quest.Template?.Conditions == null || !quest.Template.Conditions.TryGetValue(EQuestStatus.AvailableForStart, out var list) || list == null) return true;
        foreach (var c in list)
        {
            if (c is not ConditionQuest cq || string.IsNullOrEmpty(cq.target)) continue;
            var target = qc.Quests.GetConditional(cq.target);
            if (target == null) return false;   // 前置还不在任务书里
            if (!StatusMatches(cq.statuses ?? new EQuestStatus[0], target.QuestStatus)) return false;
            if (cq.availableAfter <= 0) continue;
            // 进入当前状态的时间戳都没有就没法起算 availableAfter 计时，先不接
            if (!target.StatusStartTimestamps.TryGetValue(target.QuestStatus, out var at)) return false;
            if (DateTimeExtensions.UtcNow < DateTimeExtensions.UniversalDateTimeFromUnixTime(at).AddSeconds(cq.availableAfter)) return false;
        }
        return true;
    }

    /// 09-24：要求「失败」的前置，本地的 MarkedAsFailed / FailRestartable（服务端说失败、或可重开的失败）也算失败——引擎在大厅里把服务端的 Fail 记成 MarkedAsFailed
    static bool StatusMatches(EQuestStatus[] wanted, EQuestStatus st) =>
        wanted.Contains(st) || (wanted.Contains(EQuestStatus.Fail) && (st == EQuestStatus.MarkedAsFailed || st == EQuestStatus.FailRestartable));

    static bool ChapterOpen(QuestController qc, string questId)
    {
        var chapterId = QuestFlags.ChapterOf(questId);
        if (chapterId == null) return true;
        if (QuestFlags.IsStarterOf(chapterId, questId)) return true;   // 章节的起点在开门前就得跑（09-24：踩坠机也能开陨落星辰）
        var chapter = qc.Quests.GetConditional(chapterId);
        return chapter != null && ChapterUI.ChapterStates.Begun(chapter.QuestStatus);
    }

    /// startAfter 里任一任务完成即可（一个也行）
    static bool AnyDone(QuestController qc, System.Collections.Generic.List<string> ids) =>
        ids.Any(id => qc.Quests.GetConditional(id)?.QuestStatus == EQuestStatus.Success);

    static bool AutoReady(QuestController qc, string questId)
    {
        var after = QuestFlags.StartAfter(questId);
        if (after != null) return AnyDone(qc, after);
        return QuestFlags.AutoStart(questId) && PredecessorDone(qc, questId);
    }

    /// 09-24 跟 SPT5 服务端 09-14 的改法（QuestHelper.PredecessorCompleted）：章节里既没有任务前置、也没有 startAfter 的子任务不在章节开门时一起接，
    /// 等章节列表里紧挨着的前一条结束了才开（1.1 实机：陨落星辰「找到飞机」完成后 Prapor 的 678f6bd1 才出现）。列表第一条随章节开门。
    /// 同日审查 S1 改成只看紧挨着的前一条，并在三种情况放行，否则门票主线会卡死：
    ///   ① 前一条本机没有定义（门票缺的 44 条，服务端 flags 的 missingSubs 点名）——数据不全就不拦，不去猜更前面哪条
    ///     （以前是跳过再往前找，结果「开启加固手提箱」68e2d8d5 找到了 Lightkeeper 支线 68f4d98b，而那条的前置在主线 68dac755 完成时失败，永远等不到）；
    ///   ② 前一条是分支终点（章节的 isFinisher，门票的 #15 / #47 / #62 / #69 / #84）——这是新分支的第一条，不等上一个分支的结局；
    ///   ③ 前一条已经结束，成功或失败都算，失败的不会再拦住后面。
    /// 前一条有定义但还没出现在任务书里、或还在进行中，就等。
    static bool PredecessorDone(QuestController qc, string questId)
    {
        var chapterId = QuestFlags.ChapterOf(questId);
        if (chapterId == null || chapterId == questId || QuestFlags.IsStarterOf(chapterId, questId)) return true;
        var quest = qc.Quests.GetConditional(questId);
        if (quest?.Template?.Conditions != null && quest.Template.Conditions.TryGetValue(EQuestStatus.AvailableForStart, out var gates) && gates != null
            && gates.OfType<ConditionQuest>().Any(c => !string.IsNullOrEmpty(c.target))) return true;   // 有任务前置的按前置走
        var subs = QuestFlags.SubsOf(chapterId);
        var i = subs.IndexOf(questId);
        if (i <= 0) return true;
        var prevId = subs[i - 1];
        if (QuestFlags.IsMissingSub(chapterId, prevId) || QuestFlags.Finishers(chapterId).Contains(prevId)) return true;
        var prev = qc.Quests.GetConditional(prevId);
        return prev != null && (prev.QuestStatus == EQuestStatus.Success || ChapterUI.ChapterStates.Failed(prev.QuestStatus));
    }

    public static bool Reachable(QuestController qc, Quest quest)
    {
        if (qc?.Quests == null || quest == null) return false;
        var after = QuestFlags.StartAfter(quest.Id);
        // 和 AutoReady 一致：写了 startAfter 的按 startAfter 走，不套章节顺序
        if (after != null) { if (!AnyDone(qc, after)) return false; }
        else if (!PredecessorDone(qc, quest.Id)) return false;
        if (!PrereqsMet(qc, quest)) return false;
        var chapterId = QuestFlags.ChapterOf(quest.Id);
        if (chapterId == null || chapterId == quest.Id) return true;
        var chapter = qc.Quests.GetConditional(chapterId);
        if (chapter == null) return false;
        if (ChapterUI.ChapterStates.Begun(chapter.QuestStatus)) return true;
        return chapter.QuestStatus == EQuestStatus.AvailableForStart && Reachable(qc, chapter);
    }

    public static void Rescan()
    {
        var qc = Controller; if (qc?.Quests == null) return;
        var book = qc.Quests.ToList();
        foreach (var q in book) QuestFlags.MarkStory(q);
        foreach (var q in book) Check(qc, q, live: false);
    }
}
