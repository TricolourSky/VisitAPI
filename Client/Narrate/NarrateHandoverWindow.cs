using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EFT.Dialogs;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;

namespace VisitAPI.Native;

[HarmonyPatch(typeof(ClientDialogController), nameof(ClientDialogController.ExecuteLine))]
public static class NarrateHandoverWindow
{
    static readonly Dictionary<string, Item[]> _picked = new();
    static BaseTraderDialogLine _reentry;
    static bool _open;   // 09-24 审查低项：上交窗口开着时不再为别的上交句再弹一个

    static bool Prefix(ClientDialogController __instance, BaseTraderDialogLine line, ref Task __result)
    {
        if (line != null && ReferenceEquals(line, _reentry)) { _reentry = null; return true; }
        try
        {
            var handover = line?.Actions?.OfType<DialogHandoverItemAction>().FirstOrDefault();
            if (handover == null) return true;
            var qc = __instance.questController;
            var quest = qc?.Quests?.GetConditional(handover.QuestId);
            var cond = quest?.ProgressCheckers.Keys.FirstOrDefault(c => c.id == handover.ConditionId) as ConditionItem;
            if (cond == null) return true;
            var items = qc.GetItemsForCondition(cond);
            if (items == null || items.Length == 0) return true;
            var current = quest.ProgressCheckers[cond].CurrentValue;
            var need = (double)cond.value - current;
            var have = items.Sum(i => i.StackObjectsCount);
            if (items.All(i => i.QuestItem || CurrencyUtil.IsCurrencyId(i.TemplateId)))
            {
                return true;
            }
            if (_open)
            {
                __result = Task.CompletedTask;
                return false;
            }
            Show(__instance, line, quest, cond, items, current, need, have);
            __result = Task.CompletedTask;
            return false;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[handover] Handover window takeover failed, handing over natively: " + e.Message);
            return true;
        }
    }

    static void Show(ClientDialogController dc, BaseTraderDialogLine line, Quest quest, ConditionItem cond, Item[] items, double current, double need, int have)
    {
        var key = quest.Id + "|" + cond.id;
        var picked = false;
        var screen = DialogScreenTracker.Live;
        var dialogWindow = screen != null && screen._dialogWindow != null ? screen._dialogWindow.gameObject : null;
        if (dialogWindow != null) dialogWindow.SetActive(false);

        void Closed()
        {
            _open = false;
            if (dialogWindow != null) dialogWindow.SetActive(true);
            if (picked) return;
            try
            {
                var cur = dc?.CurrentDialog;
                if (cur != null) { cur.IsBlocked = false; dc.SetCurrentDialog(dc.method_0(cur.Id)); }
            }
            catch (Exception e) { Plugin.Log.LogWarning("[handover] Failed to unblock after cancel: " + e.Message); }
        }

        _open = true;
        try
        {
            var ctx = ItemUiContext.Instance.HandoverQuestItemsWindow.Show(cond, current, items, ((IDialogContext)dc).Profile, dc.InventoryController, selected =>
            {
                if (selected == null || selected.Length == 0) return;
                picked = true;
                _picked[key] = selected;
                _reentry = line;
                if (dialogWindow != null) dialogWindow.SetActive(true);
                Run(dc, line, key);
            }, true);
            ctx.OnClose += Closed;
            ctx.OnCloseSilent += Closed;
        }
        catch
        {
            // 窗口没弹出来：把藏起来的对话窗放回来，交给 Prefix 的兜底照原生直接执行这句
            _open = false;
            if (dialogWindow != null) dialogWindow.SetActive(true);
            throw;
        }
    }

    static async void Run(ClientDialogController dc, BaseTraderDialogLine line, string key)
    {
        try { await dc.ExecuteLine(line); }
        catch (Exception e) { Plugin.Log.LogWarning("[handover] Failed to execute line after selection: " + e.Message); }
        if (_picked.Remove(key)) Plugin.Log.LogWarning($"[handover] {key} line finished but the handover action did not use the selected items, discarding this selection");
        _reentry = null;
    }

    public static void Reset() { _picked.Clear(); _reentry = null; _open = false; }

    [HarmonyPatch(typeof(ClientDialogController), nameof(ClientDialogController.method_16))]
    public static class UsePicked
    {
        static void Postfix(ClientDialogController __instance, ref Task __result)
        {
            var t = __result; if (t == null) return;
            __result = RefreshAfter(__instance, t);
        }

        static async Task RefreshAfter(ClientDialogController dc, Task handover)
        {
            try { await handover; }
            finally { Refresh(dc); }
        }

        static void Refresh(ClientDialogController dc)
        {
            try
            {
                var cur = dc?.CurrentDialog;
                if (cur == null || cur.DialogSide != EDialogSide.Player) return;
                dc.SetCurrentDialog(dc.method_0(cur.Id));
            }
            catch (Exception e) { Plugin.Log.LogWarning("[handover] Failed to rebuild options page after handover: " + e.Message); }
        }

        static bool Prefix(ClientDialogController __instance, DialogHandoverItemAction handoverAction, ref Task __result)
        {
            try
            {
                var qc = __instance.questController;
                var quest = qc?.Quests?.GetConditional(handoverAction.QuestId);
                var cond = quest?.ProgressCheckers.Keys.FirstOrDefault(c => c.id == handoverAction.ConditionId) as ConditionItem;
                if (cond == null) return true;
                var key = quest.Id + "|" + cond.id;
                if (!_picked.TryGetValue(key, out var selected)) return true;
                _picked.Remove(key);
                __result = Submit(qc, quest, cond, selected);
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[handover] Handover with selected items failed, falling back to native: " + e.Message);
                return true;
            }
        }

        static async Task Submit(QuestController qc, Quest quest, ConditionItem cond, Item[] selected)
        {
            await qc.HandoverItem(quest, cond, selected, true);
        }
    }
}
