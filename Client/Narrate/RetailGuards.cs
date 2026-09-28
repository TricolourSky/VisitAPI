using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Dialogs;
using EFT.Quests;
using HarmonyLib;

namespace VisitAPI.Native;

[HarmonyPatch(typeof(BaseTraderDialogController), "InitNewDialog")]
public static class NarrateDialogEntryGuard
{
    /// 09-24 审查 H3：整体兜住。这是引擎进对话的前缀，这里抛异常会让引擎跳过 InitNewDialog，整场对话坏掉
    static void Prefix(BaseTraderDialogController __instance, MongoID dialogId)
    {
        if (!Narrating.Now) return;
        try
        {
            if (DialogStorage.Instance != null && DialogStorage.Instance.TryGetTemplate(dialogId, out var template))
                template.CanBeFirstDialog = true;
            RetailDialogs.MarkAcquainted(__instance, __instance?.Trader?.Id);
            RetailDialogs.SeedVariables(__instance);
        }
        catch (Exception e) { Plugin.Log.LogError($"[narrate] Unlock / seeding before entering dialogue {dialogId} failed (dialogue proceeds normally): {e}"); }
    }
}

[HarmonyPatch(typeof(BaseTraderDialogController), "method_0")]
public static class NarrateSwitchGuard
{
    static void Prefix(BaseTraderDialogController __instance, ref MongoID dialogId, ref MongoID? startingPoint)
    {
        var storage = DialogStorage.Instance;
        if (storage == null || storage.TryGetTemplate(dialogId, out _)) return;
        Plugin.Log.LogWarning("[narrate] dialog template missing: " + dialogId + " - rerouting to main dialog");
        var traderId = __instance.Trader?.Id;
        GlobalConfiguration.TraderSettings settings = null;
        if (!string.IsNullOrEmpty(traderId))
            Singleton<GlobalConfiguration>.Instance?.TradersSettings?.TryGetValue(traderId, out settings);
        var main = settings?.MainDialog;
        if (main.HasValue && storage.TryGetTemplate(main.Value, out var template))
        {
            dialogId = main.Value;
            startingPoint = null;
            __instance.SetVariableValue(new DialogSetVariableAction.SaveStateData(template.MainVariable, 0));
        }
    }
}

// 1.3.4 B6：随机台词的闭区间补丁（09-24 M6 的 NarrateRandomGuard）已删——服务端加载内容包时把 1.1 的闭区间数据换成引擎原生的半开区间
//（Server\DialogueSanitizer.HalfOpenRandoms），访问内外都走原生 RandomLineCondition.Test

[HarmonyPatch(typeof(QuestStatusCondition), "Test")]
public static class NarrateQuestGhostGuard
{
    static bool Prefix(QuestStatusCondition __instance, IDialogContext context, ref bool __result)
    {
        if (!Narrating.Now) return true;
        if (context?.QuestsData != null && context.QuestsData.Any(q => q.Id == __instance.QuestId)) return true;
        __result = __instance.Statuses != null && __instance.Statuses.Contains(EQuestStatus.Locked);
        return false;
    }
}

[HarmonyPatch(typeof(DynamicTraderDialog), nameof(DynamicTraderDialog.GenerateEmbeddedQuestDialogLines))]
public static class NarrateEmbedSwitchGuard
{
    static void Postfix(DynamicTraderDialog __instance, ref IEnumerable<BaseTraderDialogLine> __result)
    {
        if (__result != null) __result = Reorder(__instance, __result);
    }

    static IEnumerable<BaseTraderDialogLine> Reorder(DynamicTraderDialog dialog, IEnumerable<BaseTraderDialogLine> lines)
    {
        foreach (var line in lines)
        {
            BaseTraderDialogLine fixedLine = null;
            try
            {
                var t = line?.Template;
                var acts = t?.Actions;
                if (line is TraderDialogTextLine && acts != null && acts.Count(a => a is DialogSwitchDialogAction) >= 2)
                {
                    var appended = new List<DialogAction>();
                    var own = new List<DialogAction>(acts);
                    for (var i = own.Count - 1; i >= 0 && appended.Count < 2; i--)
                        if (own[i] is DialogEmbedQuestDialogAction || (own[i] is DialogSwitchDialogAction && appended.Count == 1)) { appended.Insert(0, own[i]); own.RemoveAt(i); }
                    if (appended.Count == 2)
                    {
                        appended.AddRange(own);
                        var template = new DialogLineTemplate(t.Id, t.DialogSide, t.IconType, t.Trigger, appended.ToArray(), t.AnimationData);
                        fixedLine = new TraderDialogTextLine(template, dialog.Context);
                    }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("[narrate] Failed to reorder embedded quest line jumps, using as is: " + e.Message); }
            yield return fixedLine ?? line;
        }
    }
}

[HarmonyPatch(typeof(DynamicTraderDialog), MethodType.Constructor, typeof(TraderDialogTemplate), typeof(IDialogContext))]
public static class NarrateDialogBuildGuard
{
    static Exception Finalizer(Exception __exception)
    {
        if (__exception != null) Plugin.Log.LogError("[narrate] dialog build failed: " + __exception);
        return __exception;
    }
}
