using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT.Communications;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;
using UnityEngine;
using VisitAPI.ChapterUI;

namespace VisitAPI.Native;

public static class ChapterNotify
{
    /// 10-15 SORA：「1.2 的章节提醒字体是有颜色的，章节开始绿、目标完成淡蓝」——1.1.0 `QuestNotificationManager.DisplayMainQuestNotification` /
    /// `NotifyReward` 反汇编核过（1.2 现场抓的同一函数结构一样）：颜色不在预制体也不在 TextColor，是拼进消息里的富文本：
    /// 章节开始 #A1D77A（绿）、目标完成 / 章节完成 #b6e5f3（淡蓝）、失败 #f39b9b（淡红）；奖励横幅整条淡蓝，抬头和奖励分两行。
    /// 文案走原版键 `Quest/MainQuest/Notification/*`，0.16 没这些键时退回同样的措辞
    internal const string Green = "#A1D77A", Blue = "#b6e5f3", Red = "#f39b9b";
    internal static string Tint(string hex, string text) => $"<color={hex}>{text}</color>";

    public static void Handle(Quest quest)
    {
        var st = quest.QuestStatus;
        if (st == EQuestStatus.Started) Show(quest, true, Loc.Text("Quest/MainQuest/Notification/ChapterStarted", "剧情已开始", "Chapter started"), EUISoundType.QuestStarted);
        else if (st == EQuestStatus.Success) { Show(quest, true, Loc.Text("Quest/MainQuest/Notification/ChapterSuccess", "剧情完成", "Chapter complete"), EUISoundType.QuestFinished); ShowRewards(quest); }
        else if (ChapterStates.Failed(st)) Show(quest, true, Loc.Text("Quest/MainQuest/Notification/ChapterFailed", "剧情失败", "Chapter failed"), EUISoundType.QuestFailed);
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
            var text = RewardText(Loc.Text("UI/Quest/Reward/Notification/QuestChapter", "完成章节奖励：", "Reward for finishing a chapter:"), xp);
            NotificationManager.DisplayNotification(RewardBanner(title, text, chapter.Id));
        }
        catch (Exception e) { Plugin.Log.LogWarning("[banner] Reward banner failed: " + e.Message); }
    }

    /// 10-06（SORA：蓝焰、意外证人做完没弹经验）：1.1 每完成一步，把这一步完成奖励里标了 isImportant 的逐条弹出来，抬头「剧情通关奖励：」
    /// （UI/Quest/Reward/Notification/MainQuest，见 #148；1.1.0 QuestNotificationManager.NotifyReward 反汇编核过）。
    /// 原版章节本身只带成就，经验挂在步骤上（蓝焰、意外证人都在最后一步），只看章节本身的经验的话，这两章就什么都不弹。
    /// 各包里标了 isImportant 的目前只有经验，服务端加好随 flags 下发（QuestFlags.RewardXp）；战局里完成的，经验照旧撤离结算时到账
    public static void ShowStepRewards(Quest quest)
    {
        try
        {
            var xp = QuestFlags.RewardXp(quest?.Id);
            if (xp <= 0 || !Singleton<NotificationManager>.Instantiated) return;
            var chapterId = QuestFlags.ChapterOf(quest.Id);
            var title = ChapterTitle(quest, false, chapterId);
            var text = RewardText(Loc.Text("UI/Quest/Reward/Notification/MainQuest", "剧情通关奖励：", "Reward for completing the story:"), xp);
            NotificationManager.DisplayNotification(RewardBanner(title, text, chapterId));
        }
        catch (Exception e) { Plugin.Log.LogWarning("[banner] Step reward banner failed: " + e.Message); }
    }

    /// 1.1 NotifyReward 的拼法：`<color=#b6e5f3>` 抬头 `\n` 「经验： 」 数值 `</color>`，数值里的空格换成不断行空格（U+00A0），"+6 000" 不会被折行
    static string RewardText(string header, double xp) => Tint(Blue, header + "\n" + XpText(xp));

    static string XpText(double xp) =>
        Loc.Text("UI/Quest/Reward/Notification/Experience", "经验：", "Experience:") + " +" + ChapterStates.Thousands((int)xp, ' ');

    /// 1.1 章节和步骤的奖励用的是同一种横幅（NotificationQuestReward），两头外观一样：经验图标、不出声
    static ChapterBanner RewardBanner(string title, string text, string chapterId) =>
        Banner(title, text, chapter: true, chapterId, ChapterBanner.EStatus.Success, EUISoundType.QuestFinished, clip: null, silent: true, sprite: VisitArt.Load("reward_exp.png"));

    /// 横幅对象只在这里拼（10-24 收拢，旧 B10）。图没给就用章节图标；clip 没给就按 SoundType 放原生音
    static ChapterBanner Banner(string title, string text, bool chapter, string chapterId, ChapterBanner.EStatus status, EUISoundType sound, string clip, bool silent, Sprite sprite = null) => new ChapterBanner
    {
        Title = title, Text = text, IsChapter = chapter,
        Sprite = sprite ?? ChapterImages.Cached(QuestFlags.Get(chapterId)?.Icon),
        Clip = clip != null ? ChapterBundle.Clip(clip) : null, Silent = silent,
        Status = status, SoundType = sound, Duration = ENotificationDurationType.Long
    };

    public static bool IsSub(string questId) => QuestFlags.ChapterOf(questId) != null;

    public static void Show(Quest quest, bool chapter, string line, EUISoundType sound)
    {
        if (!Singleton<NotificationManager>.Instantiated) { Plugin.Log.LogWarning("[banner] NotificationManager not present, cannot show banner: " + line); return; }
        var st = quest.QuestStatus;
        var chapterId = chapter ? quest.Id : QuestFlags.ChapterOf(quest.Id);
        var clip = chapter ? (st == EQuestStatus.Started ? "story_quest_chapter_start" : st == EQuestStatus.Success ? "story_quest_chapter_end" : null)
                           : (st == EQuestStatus.Success ? "story_quest_task_done_and_reward" : ChapterStates.Failed(st) ? "story_quest_task_failed" : null);
        var status = st == EQuestStatus.Success ? ChapterBanner.EStatus.Success : ChapterStates.Failed(st) ? ChapterBanner.EStatus.Fail : ChapterBanner.EStatus.Started;
        var hue = status == ChapterBanner.EStatus.Success ? Blue : status == ChapterBanner.EStatus.Fail ? Red : Green;
        NotificationManager.DisplayNotification(Banner(ChapterTitle(quest, chapter, chapterId), Tint(hue, line), chapter, chapterId, status, sound, clip, silent: false));
    }

    public static void ShowObjective(Quest quest, string what)
    {
        if (!Singleton<NotificationManager>.Instantiated) { Plugin.Log.LogWarning("[banner] NotificationManager not present, cannot show objective banner: " + what); return; }
        var chapterId = QuestFlags.ChapterOf(quest.Id);
        NotificationManager.DisplayNotification(Banner(ChapterTitle(quest, false, chapterId), Tint(Blue, Loc.Text("Quest/MainQuest/Notification/TaskCompleted", "任务完成", "Objective complete")),
            chapter: false, chapterId, ChapterBanner.EStatus.Success, EUISoundType.QuestFinished, "story_quest_task_done_and_reward", silent: false));
    }

    /// 10-01 SORA 实测（战局里目标横幅抬头显示子任务名）：09-26 F2 之后进图时大厅控制器被释放、ChapterChain.Controller 置空，
    /// 这里查不到章节就退回子任务自己的名字。改成先问当前活的控制器（战局里是玩家那份），再退到文案表里的「章节id name」
    internal static string ChapterTitle(Quest quest, bool chapter, string chapterId)
    {
        if (!chapter && chapterId != null)
        {
            var owner = (ChapterChain.Controller ?? QuestOps.Resolve())?.Quests?.GetConditional(chapterId);
            if (owner?.Template != null) return owner.Template.Name?.Trim();
            var name = Loc.Key(chapterId + " name");
            if (name != null) return name.Trim();
        }
        return quest.Template.Name?.Trim();
    }
}

[HarmonyPatch(typeof(QuestControllerClient), nameof(QuestControllerClient.TryNotifyConditionalStatusChanged))]
public static class QuestNotify
{
    const string Name = "#FFF4D2", Bad = "#F2B0A6";

    static bool Prefix(QuestControllerClient __instance, Quest quest)
    {
        if (!Story(quest)) return true;
        if (!QuestOps.IsHideoutCopy(__instance))
            try { HandleStory(quest); }
            catch (System.Exception e) { Plugin.Log.LogError("[quest] Story notification failed: " + e); }
        return false;
    }

    static void Postfix(QuestControllerClient __instance, Quest quest)
    {
        if (Story(quest) || QuestOps.IsHideoutCopy(__instance)) return;
        try { HandleOwned(quest); }
        catch (System.Exception e) { Plugin.Log.LogError("[quest] Status notification failed (native notifications are unaffected): " + e); }
    }

    /// 剧情任务（章节 / 章节里的步骤 / isStoryQuest），原生的横幅由 Prefix 压掉、这里自己弹。隐藏任务（hidden）不算——它们在 1.1 里哪儿都不显示
    internal static bool Story(Quest quest) => quest?.Template != null && QuestFlags.IsStory(quest.Id, includeHidden: false);

    /// 剧情任务的横幅：章节走 ChapterNotify.Handle；章节里的步骤只在做完 / 失败时弹（开始不弹；变可交那一下只替「最后一个目标」补弹，见 QuestConditionNotify.Fresh）；
    /// 不属于任何章节的 isStoryQuest 什么都不弹。
    static void HandleStory(Quest quest)
    {
        if (!Singleton<NotificationManager>.Instantiated) return;
        if (QuestFlags.IsChapter(quest.Id)) { ChapterNotify.Handle(quest); return; }
        if (!ChapterNotify.IsSub(quest.Id)) return;
        var st = quest.QuestStatus;
        string status; EUISoundType sound;
        // 子任务横幅用 1.1 自己的措辞（TaskCompleted / TaskFailed），普通任务黑条的那套措辞在 HandleOwned
        if (st == EQuestStatus.Success) { status = Loc.Text("Quest/MainQuest/Notification/TaskCompleted", "任务完成", "Objective complete"); sound = EUISoundType.QuestFinished; }
        else if (ChapterStates.Failed(st)) { status = Loc.Text("Quest/MainQuest/Notification/TaskFailed", "任务失败", "Objective failed"); sound = EUISoundType.QuestFailed; }
        else if (st == EQuestStatus.AvailableForFinish) { QuestConditionNotify.Fresh(quest, last: true); return; }
        else return;
        ChapterNotify.Show(quest, false, status, sound);
        if (st == EQuestStatus.Success) ChapterNotify.ShowStepRewards(quest);
    }

    /// 剧本管到的普通任务（任务 id 出现在某份 .dlg 里）：原生横幅不开（CanShowNotificationsInGame 为 false）时补一条黑条，原生开着的让原生弹
    static void HandleOwned(Quest quest)
    {
        if (quest?.Template == null || !Singleton<NotificationManager>.Instantiated) return;
        if (!Owns(quest.Id) || quest.Template.CanShowNotificationsInGame) return;
        string status; var hue = ChapterNotify.Blue;
        EUISoundType sound;
        var st = quest.QuestStatus;
        if (st == EQuestStatus.Started) { status = Loc.Pick("任务开始", "Task started"); sound = EUISoundType.QuestStarted; }
        else if (st == EQuestStatus.AvailableForFinish) { status = Loc.Pick("任务达成要求", "Ready to hand in"); sound = EUISoundType.QuestCompleted; }
        else if (st == EQuestStatus.Success) { status = Loc.Pick("任务完成", "Task completed"); sound = EUISoundType.QuestFinished; }
        else if (ChapterStates.Failed(st)) { status = Loc.Pick("任务失败", "Task failed"); hue = Bad; sound = EUISoundType.QuestFailed; }
        else return;
        NotificationManager.DisplayNotification(new VisitBanner
        {
            Text = $"<color={Name}>{quest.Template.Name?.Trim()}</color>\n<size=88%><color={hue}>{status}</color></size>",
            SoundType = sound,
            Duration = ENotificationDurationType.Long,
        });
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
    static string _profileId;   // _done 属于哪个档案；换档案就清（10-24，不清的话登出换档会拿上一个档案的记录判「新完成」）

    static void UseCurrentProfile()
    {
        var id = VisitBackend.Profile?.Id;
        if (id == null || id == _profileId) return;
        _profileId = id;
        _done.Clear();
    }

    static HashSet<string> DoneNow(Quest quest)
    {
        var set = new HashSet<string>();
        foreach (var cond in quest.ProgressCheckers.Keys)
            if (cond != null && quest.IsConditionDone(cond)) set.Add(cond.id.ToString());
        return set;
    }

    public static void Seed(Quest quest)
    {
        try { UseCurrentProfile(); if (quest?.Template != null && !_done.ContainsKey(quest.Id) && QuestNotify.Story(quest)) _done[quest.Id] = DoneNow(quest); }
        catch (System.Exception e) { Plugin.Log.LogWarning("[quest] Failed to seed completed-objectives table: " + e.Message); }
    }

    static bool Prefix(QuestControllerClient __instance, Quest quest)
    {
        if (!QuestNotify.Story(quest)) return true;
        try { Notify(__instance, quest); }
        catch (System.Exception e) { Plugin.Log.LogError("[quest] Objective-complete notification failed: " + e); }
        return false;
    }

    static void Notify(QuestControllerClient qc, Quest quest)
    {
        if (QuestOps.IsHideoutCopy(qc)) return;
        Fresh(quest, last: false);
    }

    /// <summary>新完成的目标弹一条「任务完成」横幅（文案是目标说明）。
    /// last = 由「任务变可交」的状态事件（QuestNotify.HandleStory）调来：最后一个必要目标完成那一下引擎不发条件事件
    /// （Quest.CheckForStatusChange：条件全满足时只发状态事件，10-29 反编译核过），所以从这条路补——只替「只能对话交、又不自动交」的任务补：
    /// 它不会紧接着交掉，要回去找人谈，这一刻不弹就什么提示都没有（10-18 SORA 看门狗：「章节任务目标完成没有提示」）；自动交的紧接着就有「任务完成」横幅。
    /// 条件事件那条路（last = false）：已交 / 失败的不弹。</summary>
    internal static void Fresh(Quest quest, bool last)
    {
        if (QuestFlags.IsChapter(quest.Id) || !ChapterNotify.IsSub(quest.Id)) return;
        if (QuestFlags.Get(quest.Id)?.Story == true && !quest.Template.CanShowNotificationsInGame) return;
        UseCurrentProfile();
        var now = DoneNow(quest);
        _done.TryGetValue(quest.Id, out var seen);
        _done[quest.Id] = now;
        if (seen == null) return;
        if (last ? !QuestFlags.DialogOnly(quest.Id) || QuestFlags.AutoFinish(quest.Id) : quest.QuestStatus > EQuestStatus.AvailableForFinish) return;
        var objectives = new Dictionary<string, Condition>();
        foreach (var c in quest.Conds(EQuestStatus.AvailableForFinish)) if (c != null) objectives[c.id.ToString()] = c;
        // 一接下就满足、文案写的却是那场对话的目标（TalkPending），章节页按未完成显示，这里也不弹
        var fresh = now.Where(id => !seen.Contains(id) && objectives.ContainsKey(id) && !ChapterStates.TalkPending(quest, objectives[id])).ToList();
        if (fresh.Count == 0) return;
        var names = fresh.Select(id => objectives[id].FormattedDescription?.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
        var what = names.Count > 0 ? string.Join("; ", names) : fresh[0];
        ChapterNotify.ShowObjective(quest, what);
    }
}
