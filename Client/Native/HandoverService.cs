using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.Dialogs;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.UI;

namespace VisitAPI.Native;

public static class HandoverService
{
    static readonly Dictionary<MongoID, string> _byLine = new();

    public static void Register(MongoID lineId, string questId) => _byLine[lineId] = questId;

    public static void Watch(BaseTraderDialogController dc, QuestController quests, Profile profile, InventoryController inventory)
    {
        dc.OnDialogChanged += dialog =>
        {
            if (dialog == null) return;
            dialog.OnExecuteLine += line =>
            {
                if (line?.Template != null && _byLine.TryGetValue(line.Template.Id, out var questId)) Open(quests, profile, inventory, questId);
            };
        };
    }

    static void Open(QuestController quests, Profile profile, InventoryController inventory, string questId)
    {
        var quest = quests?.Quests?.GetConditional(questId);
        var cond = quest?.ProgressCheckers?.Keys.OfType<ConditionItem>().FirstOrDefault(c => !quest.IsConditionDone(c));
        if (cond == null) { Plugin.Log.LogDebug("[handover] nothing left to hand over for " + questId); return; }
        var items = quests.GetItemsForCondition(cond);
        if (items == null || items.Length == 0) { Plugin.Log.LogWarning("[handover] no matching items in inventory for " + questId); return; }
        var current = quest.ProgressCheckers[cond].CurrentValue;
        var screen = UnityEngine.Object.FindObjectOfType<TraderDialogScreen>();
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
