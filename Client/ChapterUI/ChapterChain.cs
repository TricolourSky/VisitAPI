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
    public static class Changed { static void Postfix(QuestController __instance, Quest conditional) => Check(__instance, conditional); }

    [HarmonyPatch(typeof(QuestController), nameof(QuestController.ManageConditional))]
    public static class Added { static void Postfix(QuestController __instance, Quest conditional) => Check(__instance, conditional); }

    static void Check(QuestController qc, Quest quest)
    {
        try { CheckCore(qc, quest); }
        catch (System.Exception e) { Plugin.Log.LogError("[chain] 自动链检查失败（任务事件本体不受影响）: " + e); }
    }

    static void CheckCore(QuestController qc, Quest quest)
    {
        if (quest?.Template == null || qc?.Quests == null) return;
        if (qc is QuestControllerClientLocalGame && Singleton<GameWorld>.Instantiated && Raid.IsHideout(Singleton<GameWorld>.Instance.LocationId))
        {
            var backend = QuestOps.Resolve();
            if (backend == null || backend is QuestControllerClientLocalGame || backend.Quests == null) { Plugin.Log.LogDebug("[chain] 藏身处事件来自 LocalGame 控制器且拿不到 Backend 版，跳过"); return; }
            var real = backend.Quests.GetConditional(quest.Id);
            if (real == null) return;
            qc = backend; quest = real;
        }
        Controller = qc; QuestFlags.MarkStory(quest);
        ChapterUI.SkipState.Use(qc.Profile?.Id);
        if (QuestFlags.IsStory(quest.Id)) { ChapterEvents.Raise(); ChapterUI.SkipState.Note(quest); }
        QuestConditionNotify.Seed(quest);
        var st = quest.QuestStatus;
        if (AutoReady(qc, quest.Id) && st == EQuestStatus.AvailableForStart && ChapterOpen(qc, quest.Id)) AutoAccept(qc, quest);
        if ((QuestFlags.IsChapter(quest.Id) || QuestFlags.AutoFinish(quest.Id)) && st == EQuestStatus.AvailableForFinish) QuestOps.Finish(qc, quest, "chain");
        var chapterId = QuestFlags.IsChapter(quest.Id) ? quest.Id : QuestFlags.ChapterOf(quest.Id);
        var chapter = chapterId == null ? null : chapterId == quest.Id ? quest : qc.Quests.GetConditional(chapterId);
        if (chapter != null && chapter.QuestStatus == EQuestStatus.AvailableForStart
            && QuestFlags.SubsOf(chapterId).Any(id => ChapterUI.ChapterStates.Begun(qc.Quests.GetConditional(id)?.QuestStatus ?? EQuestStatus.Locked))) AutoAccept(qc, chapter);
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

    static readonly System.Collections.Generic.Dictionary<string, string> _held = new();
    static void AutoAccept(QuestController qc, Quest quest)
    {
        if (!PrereqsMet(qc, quest, out var why))
        {
            if (!_held.TryGetValue(quest.Id, out var last) || last != why) { _held[quest.Id] = why; Plugin.Log.LogInfo($"[chain] {quest.Id} 引擎说可接，但前置没到，不自动接：{why}"); }
            return;
        }
        _held.Remove(quest.Id);
        QuestOps.Accept(qc, quest, "chain");
    }

    static bool PrereqsMet(QuestController qc, Quest quest, out string why)
    {
        why = null;
        if (quest.Template?.Conditions == null || !quest.Template.Conditions.TryGetValue(EQuestStatus.AvailableForStart, out var list) || list == null) return true;
        foreach (var c in list)
        {
            if (c is not ConditionQuest cq || string.IsNullOrEmpty(cq.target)) continue;
            var target = qc.Quests.GetConditional(cq.target);
            if (target == null) { why = $"前置 {cq.target} 不在任务书里"; return false; }
            var wanted = cq.statuses ?? new EQuestStatus[0];
            if (!wanted.Contains(target.QuestStatus)) { why = $"前置 {cq.target} 现在是 {target.QuestStatus}，要求 {string.Join("/", wanted)}"; return false; }
            if (cq.availableAfter <= 0) continue;
            if (!target.StatusStartTimestamps.TryGetValue(target.QuestStatus, out var at)) { why = $"前置 {cq.target} 没有进入 {target.QuestStatus} 的时间戳，定时 {cq.availableAfter}s 无从起算"; return false; }
            var due = DateTimeExtensions.UniversalDateTimeFromUnixTime(at).AddSeconds(cq.availableAfter);
            if (DateTimeExtensions.UtcNow < due) { why = $"定时未到（前置 {cq.target} 完成后 {cq.availableAfter}s），还差 {(due - DateTimeExtensions.UtcNow).TotalSeconds:0}s"; return false; }
        }
        return true;
    }

    static bool ChapterOpen(QuestController qc, string questId)
    {
        var chapterId = QuestFlags.ChapterOf(questId);
        if (chapterId == null) return true;
        var chapter = qc.Quests.GetConditional(chapterId);
        return chapter != null && ChapterUI.ChapterStates.Begun(chapter.QuestStatus);
    }

    static bool AutoReady(QuestController qc, string questId)
    {
        var after = QuestFlags.StartAfter(questId);
        if (after == null) return QuestFlags.AutoStart(questId);
        return qc.Quests.GetConditional(after)?.QuestStatus == EQuestStatus.Success;
    }

    public static bool Reachable(QuestController qc, Quest quest)
    {
        if (qc?.Quests == null || quest == null) return false;
        var after = QuestFlags.StartAfter(quest.Id);
        if (after != null && qc.Quests.GetConditional(after)?.QuestStatus != EQuestStatus.Success) return false;
        if (!PrereqsMet(qc, quest, out _)) return false;
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
        foreach (var q in book) Check(qc, q);
    }
}
