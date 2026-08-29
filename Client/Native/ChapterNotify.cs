using Comfort.Common;
using EFT.Communications;
using EFT.Quests;
using EFT.UI;
using VisitAPI.ChapterUI;

namespace VisitAPI.Native;

/// <summary>章节横幅：章节任务自己的 开始/完成/失败 和章节子任务的状态变化，都走 1.1 那条 MainQuestNotificationView（章节底图 / 子任务底图 + 对勾），
/// 标题是章节/子任务名、图标是章节图标；bundle 不在时 ChapterBanner 自己退回默认横幅。战局内外都走这条。DEV_NOTES #71/#72/#73。</summary>
public static class ChapterNotify
{
    /// 章节任务自己的状态变化：出章节横幅并吞掉（返回 true = 这是章节任务，QuestNotify 别再管）
    public static bool Handle(Quest quest)
    {
        if (!QuestFlags.IsChapter(quest.Id)) return false;
        switch (quest.QuestStatus)
        {
            case EQuestStatus.Started: Show(quest, true, Loc.Pick("新章节开始", "Chapter started"), EUISoundType.QuestStarted); break;
            case EQuestStatus.Success: Show(quest, true, Loc.Pick("章节完成", "Chapter complete"), EUISoundType.QuestFinished); break;
            case EQuestStatus.Fail:
            case EQuestStatus.MarkedAsFailed: Show(quest, true, Loc.Pick("章节失败", "Chapter failed"), EUISoundType.QuestFailed); break;
        }
        return true;
    }

    public static bool IsSub(string questId) => QuestFlags.ChapterOf(questId) != null;

    /// 章节 / 子任务共用。对勾按任务状态（可交、完成 = 勾；失败类 = 叉；其余 = 空框），图标 = 所在章节的图标。
    /// 音效照 1.1 反汇编出来的表（DEV_NOTES #73）：章节 开始/完成 专用片段、失败用普通 quest_failed；子任务 达成 = TaskFinished、失败 = TaskFailed，开始/完成 1.1 根本不出通知——横幅留着（Tech Leader 认过的样子）但不出声
    public static void Show(Quest quest, bool chapter, string line, EUISoundType sound)
    {
        if (!Singleton<NotificationManager>.Instantiated) return;
        var st = quest.QuestStatus;
        var clip = chapter ? (st == EQuestStatus.Started ? "story_quest_chapter_start" : st == EQuestStatus.Success ? "story_quest_chapter_end" : null)
                           : (st == EQuestStatus.AvailableForFinish ? "story_quest_task_done_and_reward" : st >= EQuestStatus.Fail ? "story_quest_task_failed" : null);
        NotificationManager.DisplayNotification(new ChapterBanner
        {
            Title = quest.Template.Name?.Trim(), Text = line, IsChapter = chapter,
            Sprite = ChapterImages.Cached(QuestFlags.Get(chapter ? quest.Id : QuestFlags.ChapterOf(quest.Id))?.Icon),
            Clip = clip != null ? ChapterBundle.Clip(clip) : null, Silent = !chapter && (st == EQuestStatus.Started || st == EQuestStatus.Success),
            Status = st == EQuestStatus.Success || st == EQuestStatus.AvailableForFinish ? ChapterBanner.EStatus.Success : st >= EQuestStatus.Fail ? ChapterBanner.EStatus.Fail : ChapterBanner.EStatus.Started,
            SoundType = sound, Duration = ENotificationDurationType.Long
        });
    }
}
