using System.Globalization;
using System.Linq;
using EFT;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.UI;
using VisitAPI.Dialog;

namespace VisitAPI.Native;

public static class EffectActions
{
    public static void Standing(Profile profile, TraderScreensGroup screen, string traderId, double delta)
    {
        if (!DialogParser.IsQuestId(traderId)) { Plugin.Log.LogWarning("[standing] trader id is not 24 hex characters, ignored: " + traderId); return; }
        if (double.IsNaN(delta) || double.IsInfinity(delta)) { Plugin.Log.LogWarning("[standing] delta is not a valid number, ignored: " + delta); return; }
        profile.TradersInfo.TryGetValue(new MongoID(traderId), out var info);
        var session = screen != null ? screen.TradersList?.FirstOrDefault(t => t.Id == traderId)?.Info : null;
        if (info == null && session == null) { Plugin.Log.LogWarning("[standing] trader not found: " + traderId); return; }
        var old = session?.Standing ?? info.Standing;
        // 对话效果不把声望减到 0 以下（已经是负的就不再往下减）；加声望不钳。
        // 09-24 审查低项：服务端 TraderHelper.AddStandingToTrader 是「原值 + 增量」不钳，以前客户端钳了却把原始增量发过去，两边对不上；
        // 现在发钳过之后的实际变化量
        var value = delta >= 0 ? old + delta : System.Math.Max(System.Math.Min(old, 0.0), old + delta);
        var applied = value - old;
        if (info != null) info.SetStanding(value);
        if (session != null && !ReferenceEquals(session, info)) session.SetStanding(value);
        if (applied == 0) return;
        VisitHttp.Post("/visitapi/standing/add", "{\"traderId\":\"" + traderId + "\",\"delta\":" + applied.ToString("R", CultureInfo.InvariantCulture) + "}", "[standing]");
    }

    public static void SetStatus(QuestController quests, string questId, int status)
    {
        var quest = quests?.Quests?.GetConditional(questId);
        if (quest == null) { Plugin.Log.LogWarning("[setstatus] quest not found: " + questId); return; }
        if (!System.Enum.IsDefined(typeof(EQuestStatus), status)) { Plugin.Log.LogWarning($"[setstatus] {questId}: status value {status} is not a valid quest status, ignored"); return; }
        QuestOps.SetStatus(quests, quest, (EQuestStatus)status, "setstatus");
    }

    public static void Handover(QuestController quests, Profile profile, InventoryController inventory, string questId)
    {
        var quest = quests?.Quests?.GetConditional(questId);
        var cond = QuestGates.PendingItems(quest);
        if (cond == null) return;
        var items = quests.GetItemsForCondition(cond);
        if (items == null || items.Length == 0) { Plugin.Log.LogWarning("[handover] no matching items in inventory for " + questId); return; }
        var current = quest.ProgressCheckers[cond].CurrentValue;
        var screen = DialogScreenTracker.Live;
        var dialogWindow = screen != null && screen._dialogWindow != null ? screen._dialogWindow.gameObject : null;
        if (dialogWindow != null) dialogWindow.SetActive(false);
        var ctx = ItemUiContext.Instance.HandoverQuestItemsWindow.Show(cond, current, items, profile, inventory, selected =>
        {
            if (selected == null || selected.Length == 0) return;
            quests.HandoverItem(quest, cond, selected, true)
                .ContinueWith(t => { if (t.IsFaulted) Plugin.Log.LogWarning("[handover] failed: " + t.Exception?.GetBaseException().Message); });
        }, true);
        ctx.OnClose += () => { if (dialogWindow != null) dialogWindow.SetActive(true); };
        ctx.OnCloseSilent += () => { if (dialogWindow != null) dialogWindow.SetActive(true); };
    }
}
