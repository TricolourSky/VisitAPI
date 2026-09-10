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

// ══ 兜底安全网：吞异常 / 白名单放行 / 小守卫 ══

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
        InputGuard.Release();   // 09-10：触发点开的对话锁过玩家视角的，这里放开
        if (__exception == null) Plugin.Log.LogInfo("[dlg] 对话屏关闭");
        else Plugin.Log.LogWarning("[narrate] <<< dialog screen close faulted (swallowed): " + __exception.Message);
        return null;
    }
}

/// <summary>「对话屏开着吗」的 O(1) 判据（坑 #99）：Show 记实例、Close 清、实例被绕过 Close 销毁时
/// Unity 假 null 自愈。给触发器静默用——**绝不做定时/每帧场景扫描**（T-5 铁律，战局大场景一扫就是掉帧）。</summary>
[HarmonyPatch(typeof(TraderDialogScreen), nameof(TraderDialogScreen.Show), typeof(TraderDialogScreen.TraderDialogScreenController))]
public static class DialogScreenTracker
{
    static TraderDialogScreen _live;
    /// 当前亮着的对话屏实例；关了/销毁了就是 null（Unity 假 null 自愈）
    public static TraderDialogScreen Live => _live != null && _live.isActiveAndEnabled ? _live : null;
    public static bool Open => Live != null;
    public static void Clear() => _live = null;
    static void Postfix(TraderDialogScreen __instance) { _live = __instance; Plugin.Log.LogInfo("[dlg] 对话屏打开"); }
}

// method_5 = 原生对话屏按商人 id 放行的白名单 switch, 未列入的商人会抛异常
// finalizer 吞掉该异常并对 RegisteredTraders 里的商人直接 StartDialog 兜底放行
[HarmonyPatch(typeof(TraderDialogScreen), "method_5")]
public static class WhitelistPatch
{
    public static readonly HashSet<string> RegisteredTraders = new();

    static Exception Finalizer(Exception __exception, ClientDialogController ___dialogController,
        MongoID ____traderId, MongoID? ____dialogId, ITraderAnimationController ____animationController)
    {
        if (__exception == null || !RegisteredTraders.Contains(____traderId.ToString())) return __exception;
        ___dialogController.StartDialog(____traderId, ____dialogId, ____animationController);
        return null;
    }
}

/// <summary>访问期间阻止 SetSpawnedInSession(false) 擦掉物品的 FiR 标记。</summary>
[HarmonyPatch(typeof(Profile), "SetSpawnedInSession")]
public static class FirGuard
{
    static bool Prefix(bool value)
    {
        if (value || !Narrating.Now) return true;
        Plugin.Log.LogDebug("[narrate] blocked FiR wipe during visit");
        return false;
    }
}

/// <summary>`NPCObject.Get` 查不到商人类型时，按已注册场景名反查 NPC（扩展商人枚举值不在原生表里）。</summary>
[HarmonyPatch(typeof(NPCObject), "Get")]
public static class NarrateNpcGuard
{
    static readonly FieldInfo Npcs = AccessTools.Field(typeof(NPCObject), "_npcs");

    static bool Prefix(Profile.ETraderType source, ref NPCObject __result)
    {
        if (Npcs == null) return true;   // 字段名对不上本 build 时别让每次 NPCObject.Get 都 NRE（09-07 终审）
        var npcs = (Dictionary<Profile.ETraderType, NPCObject>)Npcs.GetValue(null);
        if (npcs == null || npcs.ContainsKey(source)) return true;
        if (!TarkovApplication.NarrateController.Scenes.IsValid(source, out var info)) return true;
        var match = npcs.Values.FirstOrDefault(n => n != null && n.gameObject.scene.name == info.sceneName);
        if (match == null) return true;
        Plugin.Log.LogInfo("[narrate] NPC matched by scene for " + source);
        __result = match;
        return false;
    }
}
