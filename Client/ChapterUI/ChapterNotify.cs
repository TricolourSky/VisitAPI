using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using VisitAPI.Native;
using EFT.Communications;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;
using VisitAPI.ChapterUI;

namespace VisitAPI.Native;

public static class ChapterNotify
{
    public static bool Handle(Quest quest)
    {
        var st = quest.QuestStatus;
        if (!QuestFlags.IsChapter(quest.Id)) return false;
        if (st == EQuestStatus.Started) Show(quest, true, Loc.Pick("任务开始", "Task started"), EUISoundType.QuestStarted);
        else if (st == EQuestStatus.Success) { Show(quest, true, Loc.Pick("剧情完成", "Story complete"), EUISoundType.QuestFinished); ShowRewards(quest); }
        else if (ChapterStates.Failed(st)) Show(quest, true, Loc.Pick("剧情失败", "Story failed"), EUISoundType.QuestFailed);
        return true;
    }

    public static void ShowRewards(Quest chapter)
    {
        try
        {
            if (chapter?.Template?.Rewards == null || !Singleton<NotificationManager>.Instantiated) return;
            if (!chapter.Template.Rewards.TryGetValue(EQuestStatus.Success, out var rewards) || rewards == null) return;
            var xp = rewards.Where(r => r != null && r.type == ERewardType.Experience).Sum(r => (double)r.value);
            if (xp <= 0) return;
            var title = ChapterTitle(chapter, true, chapter.Id);
            var text = Word("UI/Quest/Reward/Notification/QuestChapter", "完成章节奖励：", "Reward for finishing a chapter:") + " "
                + Word("UI/Quest/Reward/Notification/Experience", "经验：", "Experience:") + " +"
                + ((int)xp).ToString("#,0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', ' ');
            Display(() => NotificationManager.DisplayNotification(new ChapterBanner
            {
                Title = title, Text = text, IsChapter = true,
                Sprite = VisitArt.Load("reward_exp.png") ?? ChapterImages.Cached(QuestFlags.Get(chapter.Id)?.Icon),
                Silent = true, Status = ChapterBanner.EStatus.Success, SoundType = EUISoundType.QuestFinished, Duration = ENotificationDurationType.Long
            }), $"奖励横幅「{title}」{text}");
        }
        catch (Exception e) { Plugin.Log.LogWarning("[banner] 奖励横幅失败: " + e.Message); }
    }

    static string Word(string key, string ch, string en)
    {
        try { var t = key.Localized(); if (!string.IsNullOrWhiteSpace(t) && t != key) return t; } catch { }
        return Loc.Pick(ch, en);
    }

    public static bool IsSub(string questId) => QuestFlags.ChapterOf(questId) != null;

    public static void Show(Quest quest, bool chapter, string line, EUISoundType sound)
    {
        if (!Singleton<NotificationManager>.Instantiated) { Plugin.Log.LogWarning("[banner] NotificationManager 不在，横幅出不了：" + line); return; }
        var st = quest.QuestStatus;
        var chapterId = chapter ? quest.Id : QuestFlags.ChapterOf(quest.Id);
        var title = ChapterTitle(quest, chapter, chapterId);
        var clip = chapter ? (st == EQuestStatus.Started ? "story_quest_chapter_start" : st == EQuestStatus.Success ? "story_quest_chapter_end" : null)
                           : (st == EQuestStatus.Success ? "story_quest_task_done_and_reward" : ChapterStates.Failed(st) ? "story_quest_task_failed" : null);
        var status = st == EQuestStatus.Success ? ChapterBanner.EStatus.Success : ChapterStates.Failed(st) ? ChapterBanner.EStatus.Fail : ChapterBanner.EStatus.Started;
        Display(() => NotificationManager.DisplayNotification(new ChapterBanner
        {
            Title = title, Text = line, IsChapter = chapter,
            Sprite = ChapterImages.Cached(QuestFlags.Get(chapterId)?.Icon),
            Clip = clip != null ? ChapterBundle.Clip(clip) : null, Silent = false,
            Status = status, SoundType = sound, Duration = ENotificationDurationType.Long
        }), $"{(chapter ? "章节" : "子任务")}「{title}」{line}（{st}）");
    }

    public static void ShowObjective(Quest quest, string what)
    {
        if (!Singleton<NotificationManager>.Instantiated) { Plugin.Log.LogWarning("[banner] NotificationManager 不在，目标横幅出不了：" + what); return; }
        var chapterId = QuestFlags.ChapterOf(quest.Id);
        var title = ChapterTitle(quest, false, chapterId);
        Display(() => NotificationManager.DisplayNotification(new ChapterBanner
        {
            Title = title, Text = Loc.Pick("任务完成", "Objective complete"), IsChapter = false,
            Sprite = ChapterImages.Cached(QuestFlags.Get(chapterId)?.Icon),
            Clip = ChapterBundle.Clip("story_quest_task_done_and_reward"), Silent = false,
            Status = ChapterBanner.EStatus.Success, SoundType = EUISoundType.QuestFinished, Duration = ENotificationDurationType.Long
        }), $"目标「{what}」达成（{title}）");
    }

    public static void Display(Action show, string what)
    {
        Plugin.Log.LogInfo($"[banner] 显示{(DialogScreenTracker.Open ? "（对话屏开着，画在对话屏之上）" : "")}：{what}");
        show();
    }

    internal static string ChapterTitle(Quest quest, bool chapter, string chapterId)
    {
        if (!chapter && chapterId != null)
        {
            var owner = ChapterChain.Controller?.Quests?.GetConditional(chapterId);
            if (owner?.Template != null) return owner.Template.Name?.Trim();
        }
        return quest.Template.Name?.Trim();
    }
}

[HarmonyPatch(typeof(QuestControllerClient), nameof(QuestControllerClient.TryNotifyConditionalStatusChanged))]
public static class QuestNotify
{
    const string Name = "#FFF4D2", Info = "#B6E5F3", Bad = "#F2B0A6";

    static bool Prefix(QuestControllerClient __instance, Quest quest)
    {
        if (!Story(quest)) return true;
        if (!Duplicate(__instance))
            try { Handle(quest, story: true); }
            catch (System.Exception e) { Plugin.Log.LogError("[quest] 剧情播报失败: " + e); }
        return false;
    }

    static void Postfix(QuestControllerClient __instance, Quest quest)
    {
        if (Story(quest) || Duplicate(__instance)) return;
        try { Handle(quest, story: false); }
        catch (System.Exception e) { Plugin.Log.LogError("[quest] 状态播报失败（原生通知不受影响）: " + e); }
    }

    internal static bool Duplicate(QuestControllerClient qc) =>
        qc is QuestControllerClientLocalGame && Singleton<GameWorld>.Instantiated && Raid.IsHideout(Singleton<GameWorld>.Instance.LocationId);

    internal static bool Story(Quest quest) =>
        quest?.Template != null && (QuestFlags.IsChapter(quest.Id) || ChapterNotify.IsSub(quest.Id) || QuestFlags.Get(quest.Id)?.Story == true);

    static void Handle(Quest quest, bool story)
    {
        if (quest?.Template == null) return;
        if (!Singleton<NotificationManager>.Instantiated) return;
        var chapter = QuestFlags.IsChapter(quest.Id); var sub = ChapterNotify.IsSub(quest.Id);
        if (story && !chapter && !sub) return;
        if (!chapter && !sub && !Owns(quest.Id)) return;
        if (!story && quest.Template.CanShowNotificationsInGame) return;
        if (chapter) { ChapterNotify.Handle(quest); return; }
        if (sub && (quest.QuestStatus == EQuestStatus.Started || quest.QuestStatus == EQuestStatus.AvailableForFinish)) return;
        string status; var hue = Info;
        EUISoundType sound;
        var st = quest.QuestStatus;
        if (st == EQuestStatus.Started) { status = Loc.Pick("任务开始", "Task started"); sound = EUISoundType.QuestStarted; }
        else if (st == EQuestStatus.AvailableForFinish) { status = Loc.Pick("任务达成要求", "Ready to hand in"); sound = EUISoundType.QuestCompleted; }
        else if (st == EQuestStatus.Success) { status = Loc.Pick("任务完成", "Task completed"); sound = EUISoundType.QuestFinished; }
        else if (ChapterStates.Failed(st)) { status = Loc.Pick("任务失败", "Task failed"); hue = Bad; sound = EUISoundType.QuestFailed; }
        else return;
        if (sub) { ChapterNotify.Show(quest, false, status, sound); return; }
        ChapterNotify.Display(() => NotificationManager.DisplayNotification(new VisitBanner
        {
            Text = $"<color={Name}>{quest.Template.Name?.Trim()}</color>\n<size=88%><color={hue}>{status}</color></size>",
            SoundType = sound,
            Duration = ENotificationDurationType.Long,
        }), $"黑条「{quest.Template.Name?.Trim()}」{status}");
    }

    static bool Owns(string questId) =>
        DialogFiles.All().Any(t =>
            t.TabQuestId == questId
            || t.Triggers.Any(g => g.AcceptId == questId || g.FinishId == questId || g.FailId == questId || g.IfQuestId == questId)
            || t.Nodes.Values.SelectMany(n => n.Options).Any(o =>
                o.AcceptIds.Contains(questId) || o.CompleteIds.Contains(questId) || o.HandoverId == questId
                || o.SetStatusId == questId || o.IfQuestId == questId || o.IfNotQuestId == questId));
}

[HarmonyPatch(typeof(QuestControllerClient), nameof(QuestControllerClient.TryNotifyConditionChanged))]
public static class QuestConditionNotify
{
    static readonly Dictionary<string, HashSet<string>> _done = new();

    static HashSet<string> DoneNow(Quest quest)
    {
        var set = new HashSet<string>();
        foreach (var cond in quest.ProgressCheckers.Keys)
            if (cond != null && quest.IsConditionDone(cond)) set.Add(cond.id.ToString());
        return set;
    }

    public static void Seed(Quest quest)
    {
        try { if (quest?.Template != null && !_done.ContainsKey(quest.Id) && QuestNotify.Story(quest)) _done[quest.Id] = DoneNow(quest); }
        catch (System.Exception e) { Plugin.Log.LogWarning("[quest] 目标达成表播种失败: " + e.Message); }
    }

    static bool Prefix(QuestControllerClient __instance, Quest quest)
    {
        if (!QuestNotify.Story(quest)) return true;
        try { Notify(__instance, quest); }
        catch (System.Exception e) { Plugin.Log.LogError("[quest] 目标达成提醒失败: " + e); }
        return false;
    }

    static void Notify(QuestControllerClient qc, Quest quest)
    {
        if (quest?.Template == null || QuestNotify.Duplicate(qc)) return;
        if (QuestFlags.IsChapter(quest.Id) || !ChapterNotify.IsSub(quest.Id)) return;
        if (QuestFlags.Get(quest.Id)?.Story == true && !quest.Template.CanShowNotificationsInGame) return;
        var now = DoneNow(quest);
        _done.TryGetValue(quest.Id, out var seen);
        _done[quest.Id] = now;
        if (seen == null) return;
        var objectives = new Dictionary<string, Condition>();
        if (quest.Template.Conditions != null && quest.Template.Conditions.TryGetValue(EQuestStatus.AvailableForFinish, out var list))
            foreach (var c in list) if (c != null) objectives[c.id.ToString()] = c;
        var fresh = now.Where(id => !seen.Contains(id) && objectives.ContainsKey(id)).ToList();
        if (fresh.Count == 0) return;
        var necessary = objectives.Values.Where(c => c.IsNecessary).Select(c => c.id.ToString()).ToList();
        if (quest.QuestStatus >= EQuestStatus.AvailableForFinish || (necessary.Count > 0 && necessary.All(now.Contains)))
        { Plugin.Log.LogInfo($"[quest] 目标全部达成，交给任务完成横幅：{quest.Id}"); return; }
        var names = fresh.Select(id => objectives[id].FormattedDescription?.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
        var what = names.Count > 0 ? string.Join("; ", names) : fresh[0];
        Plugin.Log.LogInfo($"[quest] 目标达成：{quest.Id} → {what}");
        ChapterNotify.ShowObjective(quest, what);
    }
}
