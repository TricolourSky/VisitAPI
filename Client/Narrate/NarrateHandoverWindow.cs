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
            if (!TryResolve(qc, handover, out var quest, out var cond, out var key)) return true;
            var items = qc.GetItemsForCondition(cond);
            if (items == null || items.Length == 0) return true;
            var current = quest.ProgressCheckers[cond].CurrentValue;
            if (items.All(i => i.QuestItem || CurrencyUtil.IsCurrencyId(i.TemplateId))) return true;
            if (!_open) Show(__instance, line, cond, key, items, current);
            __result = Task.CompletedTask;
            return false;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[handover] Handover window takeover failed, handing over natively: " + e.Message);
            return true;
        }
    }

    /// <summary>上交动作指的那条任务和那个「上交物品」条件，以及记选择用的键 任务|条件。
    /// 任务不在任务书里、条件不是物品条件都算没解析出来。</summary>
    static bool TryResolve(QuestController qc, DialogHandoverItemAction action, out Quest quest, out ConditionItem cond, out string key)
    {
        quest = qc?.Quests?.GetConditional(action.QuestId);
        cond = quest?.ProgressCheckers.Keys.FirstOrDefault(c => c.id == action.ConditionId) as ConditionItem;
        key = cond == null ? null : quest.Id + "|" + cond.id;
        return cond != null;
    }

    static void Show(ClientDialogController dc, BaseTraderDialogLine line, ConditionItem cond, string key, Item[] items, double current)
    {
        var picked = false;
        var screen = DialogScreenTracker.Live;
        var dialogWindow = screen != null && screen._dialogWindow != null ? screen._dialogWindow.gameObject : null;
        if (dialogWindow != null) dialogWindow.SetActive(false);

        void Closed()
        {
            _open = false;
            if (dialogWindow != null) dialogWindow.SetActive(true);
            if (!picked) DialogPage.Reopen(dc, "[handover]");
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
                // 选完了把这句再执行一遍（上面的前缀认出 _reentry 放行）；这句跑完 _picked 里还有这份选择 = 上交动作没用上它，丢掉
                DialogPage.ExecuteAgain(dc, line, e => "[handover] Failed to execute line after selection: " + e.Message, () =>
                {
                    if (_picked.Remove(key)) Plugin.Log.LogWarning($"[handover] {key} line finished but the handover action did not use the selected items, discarding this selection");
                    _reentry = null;
                });
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

    public static void Reset() { _picked.Clear(); _reentry = null; _open = false; }

    [HarmonyPatch(typeof(ClientDialogController), nameof(ClientDialogController.method_16))]
    public static class UsePicked
    {
        static void Postfix(ClientDialogController __instance, ref Task __result)
        {
            if (__result != null) __result = RefreshAfter(__instance, __result);
        }

        static async Task RefreshAfter(ClientDialogController dc, Task handover)
        {
            try { await handover; }
            finally { Refresh(dc); }
        }

        /// 上交完把选项页重画一遍（只重画玩家选项页；商人说话那一页重建会把台词再播一遍）
        static void Refresh(ClientDialogController dc)
        {
            try { DialogPage.Redraw(dc, playerOnly: true); }
            catch (Exception e) { Plugin.Log.LogWarning("[handover] Failed to rebuild options page after handover: " + e.Message); }
        }

        static bool Prefix(ClientDialogController __instance, DialogHandoverItemAction handoverAction, ref Task __result)
        {
            try
            {
                var qc = __instance.questController;
                if (!TryResolve(qc, handoverAction, out var quest, out var cond, out var key)) return true;
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
