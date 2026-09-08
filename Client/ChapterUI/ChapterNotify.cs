using System.Linq;
using Comfort.Common;
using EFT.Communications;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;
using VisitAPI.ChapterUI;

namespace VisitAPI.Native;

/// <summary>章节横幅：章节任务自己的 开始/完成/失败 和子任务的状态变化，都走 1.1 那条 MainQuestNotificationView
/// （章节底图/子任务底图 + 对勾），标题是章节名、图标是章节图标；bundle 不在时 ChapterBanner 自己退回默认横幅。
/// 失败口径统一走 ChapterStates.Failed（B6/B7）。DEV_NOTES #71/#72/#73。</summary>
public static class ChapterNotify
{
    /// 章节任务自己的状态变化：出章节横幅并吞掉（返回 true = 这是章节任务，QuestNotify 别再管）
    public static bool Handle(Quest quest)
    {
        var st = quest.QuestStatus;
        if (!QuestFlags.IsChapter(quest.Id)) return false;
        if (st == EQuestStatus.Started) Show(quest, true, Loc.Pick("任务开始", "Task started"), EUISoundType.QuestStarted);
        else if (st == EQuestStatus.Success) Show(quest, true, Loc.Pick("剧情完成", "Story complete"), EUISoundType.QuestFinished);
        else if (ChapterStates.Failed(st)) Show(quest, true, Loc.Pick("剧情失败", "Story failed"), EUISoundType.QuestFailed);
        return true;
    }

    public static bool IsSub(string questId) => QuestFlags.ChapterOf(questId) != null;

    /// 章节/子任务共用。对勾按任务状态（可交、完成=勾；失败家族=叉；其余=空框），图标=所在章节的图标。
    /// ⚠️ **标题永远是章节名，子任务名一个字都不露**——正式版如此（截图实证 `Boreas / Objective complete`），
    /// 而且作者常给子任务起开发用内部名，露出去就剧透。音效照 1.1 反汇编的表（DEV_NOTES #73）。
    public static void Show(Quest quest, bool chapter, string line, EUISoundType sound)
    {
        if (!Singleton<NotificationManager>.Instantiated) return;
        var st = quest.QuestStatus;
        var chapterId = chapter ? quest.Id : QuestFlags.ChapterOf(quest.Id);
        var clip = chapter ? (st == EQuestStatus.Started ? "story_quest_chapter_start" : st == EQuestStatus.Success ? "story_quest_chapter_end" : null)
                           : (st == EQuestStatus.Success ? "story_quest_task_done_and_reward" : ChapterStates.Failed(st) ? "story_quest_task_failed" : null);
        NotificationManager.DisplayNotification(new ChapterBanner
        {
            Title = ChapterTitle(quest, chapter, chapterId), Text = line, IsChapter = chapter,
            Sprite = ChapterImages.Cached(QuestFlags.Get(chapterId)?.Icon),
            Clip = clip != null ? ChapterBundle.Clip(clip) : null, Silent = false,
            Status = st == EQuestStatus.Success || st == EQuestStatus.AvailableForFinish ? ChapterBanner.EStatus.Success : ChapterStates.Failed(st) ? ChapterBanner.EStatus.Fail : ChapterBanner.EStatus.Started,
            SoundType = sound, Duration = ENotificationDurationType.Long
        });
    }

    /// 横幅标题：一律取所属章节的任务名。捞不到（flags 没到/章节不在书里）才退回这条任务自己的名字——总比空标题强
    static string ChapterTitle(Quest quest, bool chapter, string chapterId)
    {
        if (!chapter && chapterId != null)
        {
            var owner = ChapterChain.Controller?.Quests?.GetConditional(chapterId);
            if (owner?.Template != null) return owner.Template.Name?.Trim();
        }
        return quest.Template.Name?.Trim();
    }
}

/// <summary>VisitAPI 任务把 canShowNotificationsInGame 关掉后，原生对它们整个闭嘴（那道开关逐任务，见
/// QuestControllerClient.TryNotifyConditionalStatusChanged 第一行 if）。这里接管这些任务的状态播报：
/// 章节/子任务走 1.1 章节横幅，其余 VisitAPI 任务走自家黑条。不是 .dlg 里出现过、也不是章节家族的一律不碰，SPT 原生提醒零影响。</summary>
[HarmonyPatch(typeof(QuestControllerClient), nameof(QuestControllerClient.TryNotifyConditionalStatusChanged))]
public static class QuestNotify
{
    // 正式服配色：任务名米白、状态行浅蓝（从 1.10.1 截图直接量的）
    const string Name = "#FFF4D2", Info = "#B6E5F3", Bad = "#F2B0A6";

    /// 09-07：1.1 的剧情任务 `canShowNotificationsInGame` 多半是 true，原生就会弹「次要任务已完成: <名字键>」（剧情任务名字为空，
    /// 露出来的是 id）。剧情家族（章节 / 子任务 / isStoryQuest）一律由我们接管：前缀吞掉原生那条，只出 1.1 的章节横幅。
    static bool Prefix(Quest quest)
    {
        if (!Story(quest)) return true;
        try { Handle(quest, story: true); }
        catch (System.Exception e) { Plugin.Log.LogError("[quest] 剧情播报失败: " + e); }
        return false;
    }

    // 阶段四单点隔离（B8 同款）：这补丁挂在任务状态事件派发上，自己炸会打断原生通知链
    static void Postfix(Quest quest)
    {
        if (Story(quest)) return;   // 前缀已经处理过（Harmony 跳过原方法后后缀照样跑）
        try { Handle(quest, story: false); }
        catch (System.Exception e) { Plugin.Log.LogError("[quest] 状态播报失败（原生通知不受影响）: " + e); }
    }

    internal static bool Story(Quest quest) =>
        quest?.Template != null && (QuestFlags.IsChapter(quest.Id) || ChapterNotify.IsSub(quest.Id) || QuestFlags.Get(quest.Id)?.Story == true);

    static void Handle(Quest quest, bool story)
    {
        if (quest?.Template == null) return;
        // 「达成即解锁商人」和横幅是两码事：只看任务 JSON 的 unlockTraderOnReady 开关，
        // 不要求这条任务在 .dlg 里出现过——从商人任务列表接的普通任务照样该解锁（DEV_NOTES #67/#80）
        if (quest.QuestStatus == EQuestStatus.AvailableForFinish && QuestFlags.Unlock(quest.Id)) ReportReady(quest.Id);
        if (!Singleton<NotificationManager>.Instantiated) return;
        var chapter = QuestFlags.IsChapter(quest.Id); var sub = ChapterNotify.IsSub(quest.Id);
        if (story && !chapter && !sub) return;   // 1.1 标了 isStoryQuest 但不在任何章节里的隐藏任务：一声不吭
        if (!chapter && !sub && !Owns(quest.Id)) return;
        if (!story && quest.Template.CanShowNotificationsInGame) return;   // 作者没关原生通知：原生自己会报，我们不叠一条（否则双响）
        if (chapter) { ChapterNotify.Handle(quest); return; }
        // 剧情线只报三件事：剧情开始/任务完成（子任务 Success）/剧情完成，外加子任务失败。**中间态一律不弹**
        // （子任务的「任务开始」「达成要求」都算中间态）——Tech Leader 08-30 拍板，正式版一次事件一条横幅。
        if (sub && (quest.QuestStatus == EQuestStatus.Started || quest.QuestStatus == EQuestStatus.AvailableForFinish)) return;
        string status; var hue = Info;
        EUISoundType sound;
        var st = quest.QuestStatus;
        if (st == EQuestStatus.Started) { status = Loc.Pick("任务开始", "Task started"); sound = EUISoundType.QuestStarted; }
        else if (st == EQuestStatus.AvailableForFinish) { status = Loc.Pick("任务达成要求", "Ready to hand in"); sound = EUISoundType.QuestCompleted; }
        else if (st == EQuestStatus.Success) { status = Loc.Pick("任务完成", "Task completed"); sound = EUISoundType.QuestFinished; }
        else if (ChapterStates.Failed(st)) { status = Loc.Pick("任务失败", "Task failed"); hue = Bad; sound = EUISoundType.QuestFailed; }
        else return;
        // 章节的子任务走 1.1 章节横幅（子任务底图+对勾）；其余 VisitAPI 任务走自家黑条
        if (sub) { ChapterNotify.Show(quest, false, status, sound); return; }
        NotificationManager.DisplayNotification(new VisitBanner
        {
            Text = $"<color={Name}>{quest.Template.Name?.Trim()}</color>\n<size=88%><color={hue}>{status}</color></size>",
            SoundType = sound,
            Duration = ENotificationDurationType.Long,
            ShowImmediately = true
        });
    }

    static void ReportReady(string questId) => VisitHttp.Post("/visitapi/quest/ready", "{\"questId\":\"" + questId + "\"}", "[quest] ready");

    // .dlg 里出现过的任务 id 才算 VisitAPI 的。每次现扫（状态变化没几次），热重载改剧本也立刻生效
    static bool Owns(string questId) =>
        DialogFiles.All().Any(t =>
            t.TabQuestId == questId
            || t.Triggers.Any(g => g.AcceptId == questId || g.FinishId == questId || g.FailId == questId || g.IfQuestId == questId)
            || t.Nodes.Values.SelectMany(n => n.Options).Any(o =>
                o.AcceptIds.Contains(questId) || o.CompleteIds.Contains(questId) || o.HandoverId == questId
                || o.SetStatusId == questId || o.IfQuestId == questId || o.IfNotQuestId == questId));
}

/// <summary>09-07：0.16 在**目标**达成时另有一条原生提醒「次要任务已完成: <任务名>」（QuestControllerClient.TryNotifyConditionChanged，
/// 与任务状态提醒是两个入口）。剧情任务名字为空，露出来的是「…… name」键名。剧情家族的目标进度一律不走原生提醒——
/// 1.1 里目标勾选只在剧情页里体现，横幅只在子任务完成时出一次（QuestNotify 那条）。</summary>
[HarmonyPatch(typeof(QuestControllerClient), nameof(QuestControllerClient.TryNotifyConditionChanged))]
public static class QuestConditionNotify
{
    static bool Prefix(Quest quest) => !QuestNotify.Story(quest);
}
