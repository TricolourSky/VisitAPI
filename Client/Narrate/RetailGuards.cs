using System;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Dialogs;
using EFT.Quests;
using HarmonyLib;

namespace VisitAPI.Native;

// ══ 零售对话数据相对正式服的缺口补全（只在访问中生效）══

/// <summary>访问中开新对话：解锁模板 + 播种会话变量（"相识"标志等）。</summary>
[HarmonyPatch(typeof(BaseTraderDialogController), "InitNewDialog")]
public static class NarrateDialogEntryGuard
{
    static void Prefix(BaseTraderDialogController __instance, MongoID dialogId)
    {
        if (!Narrating.Now) return;
        if (DialogStorage.Instance != null && DialogStorage.Instance.TryGetTemplate(dialogId, out var template))
            template.CanBeFirstDialog = true;
        RetailDialogs.MarkAcquainted(__instance, __instance.Trader?.Id);   // 先写档案（0→1），再种会话（档案有值的不再盖）
        RetailDialogs.SeedVariables(__instance);
        Plugin.Log.LogDebug("[narrate] dialog entry unlocked + session variables seeded for " + dialogId);
    }
}

/// <summary>
/// 1.0 零售数据里存在悬空的 SwitchDialog 目标（模板只在正式服服务端库里）——
/// 原生 method_0 对缺失模板直接炸且异常被点击管线吞掉，表现为对话转圈假锁。缺失时改道回商人主对话入口。
/// </summary>
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

/// <summary>访问中随机行条件恒真（零售数据的随机行在 0.16 环境下掷不出来）。</summary>
[HarmonyPatch(typeof(RandomLineCondition), "Test")]
public static class NarrateRandomGuard
{
    static bool Prefix(ref bool __result)
    {
        if (!Narrating.Now) return true;
        __result = true;
        return false;
    }
}

/// <summary>
/// 库外任务（零售数据引用、但 SPT 数据库里没有的任务）的状态条件统一模拟成 Locked——
/// 互斥分支才不会同时放行（旧 DEV_NOTES #49）。别按商人加覆写表：Skier 试过，反而顶掉正常开场白（#58）。
/// </summary>
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

/// <summary>点击管线会静默吞构建异常，此处 LogError 是唯一现场可见性。</summary>
[HarmonyPatch(typeof(DynamicTraderDialog), MethodType.Constructor, typeof(TraderDialogTemplate), typeof(IDialogContext))]
public static class NarrateDialogBuildGuard
{
    static Exception Finalizer(Exception __exception)
    {
        if (__exception != null) Plugin.Log.LogError("[narrate] dialog build failed: " + __exception);
        return __exception;
    }
}
