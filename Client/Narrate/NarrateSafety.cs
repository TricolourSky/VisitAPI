using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Dialogs;
using EFT.UI;
using HarmonyLib;

namespace VisitAPI.Native;

[HarmonyPatch(typeof(BetterAudio), nameof(BetterAudio.ToggleNarrate))]
public static class NarrateAudioGuard
{
    static Exception Finalizer(Exception __exception)
    {
        if (__exception != null)
            Plugin.Log.LogWarning("[narrate] audio toggle skipped (" + __exception.GetType().Name + "): " + __exception.Message);
        return null;
    }
}

[HarmonyPatch(typeof(TraderDialogScreen), "Close")]
public static class DialogScreenCloseGuard
{
    static Exception Finalizer(Exception __exception)
    {
        DialogScreenTracker.Clear();
        InputGuard.Release();
        NarrateHandoverWindow.Reset();
        NarrateChoiceWindow.Reset();
        if (__exception != null) Plugin.Log.LogWarning("[narrate] <<< dialog screen close faulted (swallowed): " + __exception.Message);
        return null;
    }
}

[HarmonyPatch(typeof(TraderDialogScreen), nameof(TraderDialogScreen.Show), typeof(TraderDialogScreen.TraderDialogScreenController))]
public static class DialogScreenTracker
{
    static TraderDialogScreen _live;
    public static TraderDialogScreen Live => _live != null && _live.isActiveAndEnabled ? _live : null;
    public static bool Open => Live != null;
    public static void Clear() => _live = null;
    static void Postfix(TraderDialogScreen __instance) { _live = __instance; }
}

[HarmonyPatch(typeof(TraderDialogScreen), "method_5")]
public static class WhitelistPatch
{
    public static readonly HashSet<string> RegisteredTraders = new();

    // 引擎 method_5 先挂事件、打开对话窗，最后按白名单 switch，白名单外的商人抛这条
    const string Unlisted = "Unable to find trader controller for trader: ";

    /// 09-24 审查低项：只接管「白名单外的商人」这一种异常。以前 method_5 抛任何异常都当成白名单问题接着 StartDialog，
    /// 比如商人不在档案里（TradersInfo 取不到）时对话窗根本没打开，照样开对话
    static Exception Finalizer(Exception __exception, ClientDialogController ___dialogController,
        MongoID ____traderId, MongoID? ____dialogId, ITraderAnimationController ____animationController)
    {
        if (__exception == null || !RegisteredTraders.Contains(____traderId.ToString())) return __exception;
        if (__exception.GetType() != typeof(Exception) || __exception.Message == null || !__exception.Message.StartsWith(Unlisted, StringComparison.Ordinal))
        {
            Plugin.Log.LogError($"[narrate] Dialog screen init for {____traderId} threw a non-whitelist exception, not taking over: {__exception}");
            return __exception;
        }
        try { ___dialogController.StartDialog(____traderId, ____dialogId, ____animationController); }
        catch (Exception e)
        {
            Plugin.Log.LogError($"[narrate] Failed to start dialog for non-whitelisted trader {____traderId}: {e}");
            return e;
        }
        return null;
    }
}

[HarmonyPatch(typeof(Profile), "SetSpawnedInSession")]
public static class FirGuard
{
    static bool Prefix(bool value)
    {
        if (value || !Narrating.Now) return true;
        return false;
    }
}

[HarmonyPatch(typeof(NPCObject), "Get")]
public static class NarrateNpcGuard
{
    static readonly FieldInfo Npcs = AccessTools.Field(typeof(NPCObject), "_npcs");

    static bool Prefix(Profile.ETraderType source, ref NPCObject __result)
    {
        if (Npcs == null) return true;
        var npcs = (Dictionary<Profile.ETraderType, NPCObject>)Npcs.GetValue(null);
        if (npcs == null || npcs.ContainsKey(source)) return true;
        if (!TarkovApplication.NarrateController.Scenes.IsValid(source, out var info)) return true;
        var match = npcs.Values.FirstOrDefault(n => n != null && n.gameObject.scene.name == info.sceneName);
        if (match == null) return true;
        __result = match;
        return false;
    }
}
