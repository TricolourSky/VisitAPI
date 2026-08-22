using System.Linq;
using Comfort.Common;
using EFT.Communications;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;

namespace VisitAPI.Native;

// VisitAPI 的任务把 canShowNotificationsInGame 关掉后，原生对它们整个闭嘴（那道开关是逐任务的，
// 见 QuestControllerClient.TryNotifyConditionalStatusChanged 的第一行 if）。这里接管这些任务的
// 状态播报，改用 VisitAPI 自己的横幅。不是 .dlg 里出现过的任务一律不碰，SPT 原生提醒零影响。
[HarmonyPatch(typeof(QuestControllerClient), nameof(QuestControllerClient.TryNotifyConditionalStatusChanged))]
public static class QuestNotify
{
    // 正式服的配色：任务名米白、状态行浅蓝（从 1.10.1 截图上直接量的）
    const string Name = "#FFF4D2", Info = "#B6E5F3", Bad = "#F2B0A6";

    static void Postfix(Quest quest)
    {
        if (quest?.Template == null || !Singleton<NotificationManager>.Instantiated || !Owns(quest.Id)) return;
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
        NotificationManager.DisplayNotification(new VisitBanner
        {
            Text = $"<color={Name}>{quest.Template.Name?.Trim()}</color>\n<size=88%><color={hue}>{status}</color></size>",
            SoundType = sound,
            Duration = ENotificationDurationType.Long,
            ShowImmediately = true
        });
    }

    // .dlg 里出现过的任务 id 才算 VisitAPI 的。每次现扫（状态变化本来就没几次），
    // 热重载改了剧本也立刻生效，不用管缓存失效。
    static bool Owns(string questId) =>
        DialogFiles.Loader.TraderIds().Select(DialogFiles.Loader.Load).Where(t => t != null).Any(t =>
            t.TabQuestId == questId
            || t.Triggers.Any(g => g.AcceptId == questId || g.IfQuestId == questId)
            || t.Nodes.Values.SelectMany(n => n.Options).Any(o =>
                o.AcceptId == questId || o.CompleteId == questId || o.HandoverId == questId
                || o.SetStatusId == questId || o.IfQuestId == questId || o.IfNotQuestId == questId));
}
