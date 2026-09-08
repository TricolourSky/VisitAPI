using System.Globalization;
using System.Linq;
using EFT;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.UI;

namespace VisitAPI.Native;

/// <summary>选项副作用的执行体（由 DialogSession 分发调用）。</summary>
public static class EffectActions
{
    public static void Standing(Profile profile, TraderScreensGroup screen, string traderId, double delta)
    {
        profile.TradersInfo.TryGetValue(new MongoID(traderId), out var info);
        var session = screen != null ? screen.TradersList?.FirstOrDefault(t => t.Id == traderId)?.Info : null;
        if (info == null && session == null) { Plugin.Log.LogWarning("[standing] trader not found: " + traderId); return; }
        var value = System.Math.Max(0.0, (session?.Standing ?? info.Standing) + delta);
        if (info != null) info.SetStanding(value);
        if (session != null && !ReferenceEquals(session, info)) session.SetStanding(value);
        Plugin.Log.LogDebug($"[standing] {traderId} {(delta >= 0 ? "+" : "")}{delta} -> {value:0.###}");
        VisitHttp.Post("/visitapi/standing/add", "{\"traderId\":\"" + traderId + "\",\"delta\":" + delta.ToString(CultureInfo.InvariantCulture) + "}", "[standing]");
    }

    public static void SetStatus(QuestController quests, string questId, int status)
    {
        var quest = quests?.Quests?.GetConditional(questId);
        if (quest == null) { Plugin.Log.LogWarning("[setstatus] quest not found: " + questId); return; }
        if (QuestOps.SetStatus(quests, quest, (EQuestStatus)status, "setstatus"))   // T-6 唯一出口：任务事务在途时拒写
            Plugin.Log.LogDebug($"[setstatus] {questId} -> {(EQuestStatus)status} (now {quest.QuestStatus})");
    }

    public static void Handover(QuestController quests, Profile profile, InventoryController inventory, string questId)
    {
        var quest = quests?.Quests?.GetConditional(questId);
        var cond = QuestGates.PendingItems(quest);
        if (cond == null) { Plugin.Log.LogDebug("[handover] nothing left to hand over for " + questId); return; }
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
            Plugin.Log.LogDebug($"[handover] {questId} submitted {selected.Sum(i => i.StackObjectsCount)} item(s)");
        }, true);
        ctx.OnClose += () => { if (dialogWindow != null) dialogWindow.SetActive(true); };
        ctx.OnCloseSilent += () => { if (dialogWindow != null) dialogWindow.SetActive(true); };
    }
}
