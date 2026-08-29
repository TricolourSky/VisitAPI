using System.Linq;
using Comfort.Common;
using EFT.Communications;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;

namespace VisitAPI.Native;

// VisitAPI 的任务把 canShowNotificationsInGame 关掉后，原生对它们整个闭嘴（那道开关是逐任务的，
// 见 QuestControllerClient.TryNotifyConditionalStatusChanged 的第一行 if）。这里接管这些任务的
// 状态播报，改用 VisitAPI 自己的横幅。不是 .dlg 里出现过的任务、也不是章节/子任务的一律不碰，SPT 原生提醒零影响。
[HarmonyPatch(typeof(QuestControllerClient), nameof(QuestControllerClient.TryNotifyConditionalStatusChanged))]
public static class QuestNotify
{
    // 正式服的配色：任务名米白、状态行浅蓝（从 1.10.1 截图上直接量的）
    const string Name = "#FFF4D2", Info = "#B6E5F3", Bad = "#F2B0A6";

    static void Postfix(Quest quest)
    {
        if (quest?.Template == null) return;
        // 「达成即解锁商人」和横幅是两码事：只看任务 JSON 里的 unlockTraderOnReady 开关，
        // 不要求这条任务在 .dlg 里出现过 —— 从商人任务列表接的普通任务照样该解锁（DEV_NOTES #67/#80）
        if (quest.QuestStatus == EQuestStatus.AvailableForFinish && QuestFlags.Unlock(quest.Id)) ReportReady(quest.Id);
        if (!Singleton<NotificationManager>.Instantiated) return;
        var chapter = QuestFlags.IsChapter(quest.Id); var sub = ChapterNotify.IsSub(quest.Id);
        if (!chapter && !sub && !Owns(quest.Id)) return;
        if (quest.Template.CanShowNotificationsInGame) return;   // 作者没关原生通知：原生自己会报，我们不再叠一条（否则双响）
        if (chapter) { ChapterNotify.Handle(quest); return; }
        string status, hue = Info;
        EUISoundType sound;
        switch (quest.QuestStatus)
        {
            case EQuestStatus.Started:
                status = Loc.Pick("任务开始", "Task started"); sound = EUISoundType.QuestStarted; break;
            case EQuestStatus.AvailableForFinish:
                status = Loc.Pick("任务达成要求", "Ready to hand in"); sound = EUISoundType.QuestCompleted; break;
            case EQuestStatus.Success:
                status = Loc.Pick("任务完成", "Task completed"); sound = EUISoundType.QuestFinished; break;
            case EQuestStatus.Fail:
            case EQuestStatus.MarkedAsFailed:
            case EQuestStatus.FailRestartable:
                status = Loc.Pick("任务失败", "Task failed"); hue = Bad; sound = EUISoundType.QuestFailed; break;
            default:
                return;
        }
        // 章节的子任务走 1.1 那条章节横幅（子任务底图 + 对勾）；其余 VisitAPI 任务走自家的黑条
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

    // .dlg 里出现过的任务 id 才算 VisitAPI 的。每次现扫（状态变化本来就没几次），
    // 热重载改了剧本也立刻生效，不用管缓存失效。
    static bool Owns(string questId) =>
        DialogFiles.All().Any(t =>
            t.TabQuestId == questId
            || t.Triggers.Any(g => g.AcceptId == questId || g.FinishId == questId || g.FailId == questId || g.IfQuestId == questId)
            || t.Nodes.Values.SelectMany(n => n.Options).Any(o =>
                o.AcceptIds.Contains(questId) || o.CompleteIds.Contains(questId) || o.HandoverId == questId
                || o.SetStatusId == questId || o.IfQuestId == questId || o.IfNotQuestId == questId));
}
