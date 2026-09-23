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
                Plugin.Log.LogInfo($"[handover] 任务 {quest.Id} 条件 {cond.id}：要交的全是任务物品/货币，照原生直接交（仓库 {have}，还差 {need}）");
                return true;
            }
            Show(__instance, line, quest, cond, items, current, need, have);
            __result = Task.CompletedTask;
            return false;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[handover] 上交窗口接管失败，照原生直接交: " + e.Message);
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
        Plugin.Log.LogInfo($"[handover] 任务 {quest.Id} 条件 {cond.id}：仓库有 {have} 件，还差 {need}，先弹上交选择窗口，这句台词等选完再执行");

        void Closed()
        {
            if (dialogWindow != null) dialogWindow.SetActive(true);
            if (picked) return;
            try
            {
                var cur = dc?.CurrentDialog;
                if (cur != null) { cur.IsBlocked = false; dc.SetCurrentDialog(dc.method_0(cur.Id)); }
                Plugin.Log.LogInfo($"[handover] {quest.Id} 窗口关了没选物品，这句台词不执行，已解锁并重建选项页");
            }
            catch (Exception e) { Plugin.Log.LogWarning("[handover] 取消后解锁失败: " + e.Message); }
        }

        var ctx = ItemUiContext.Instance.HandoverQuestItemsWindow.Show(cond, current, items, ((IDialogContext)dc).Profile, dc.InventoryController, selected =>
        {
            if (selected == null || selected.Length == 0) return;
            picked = true;
            _picked[key] = selected;
            _reentry = line;
            if (dialogWindow != null) dialogWindow.SetActive(true);
            Plugin.Log.LogInfo($"[handover] {quest.Id} 选了 {selected.Sum(i => i.StackObjectsCount)} 件，执行这句台词");
            Run(dc, line, key);
        }, true);
        ctx.OnClose += Closed;
        ctx.OnCloseSilent += Closed;
    }

    static async void Run(ClientDialogController dc, BaseTraderDialogLine line, string key)
    {
        try { await dc.ExecuteLine(line); }
        catch (Exception e) { Plugin.Log.LogWarning("[handover] 选完后执行台词失败: " + e.Message); }
        if (_picked.Remove(key)) Plugin.Log.LogWarning($"[handover] {key} 这句执行完了但上交动作没用到选中的物品，作废这次选择");
        _reentry = null;
    }

    public static void Reset() { _picked.Clear(); _reentry = null; }

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
                Plugin.Log.LogInfo($"[handover] 上交事务结束，选项页 {cur.Id} 按最新任务状态重建");
            }
            catch (Exception e) { Plugin.Log.LogWarning("[handover] 上交后重建选项页失败: " + e.Message); }
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
                Plugin.Log.LogWarning("[handover] 按选中物品上交失败，照原生: " + e.Message);
                return true;
            }
        }

        static async Task Submit(QuestController qc, Quest quest, ConditionItem cond, Item[] selected)
        {
            await qc.HandoverItem(quest, cond, selected, true);
            Plugin.Log.LogInfo($"[handover] {quest.Id} 交了 {selected.Sum(i => i.StackObjectsCount)} 件");
        }
    }
}
