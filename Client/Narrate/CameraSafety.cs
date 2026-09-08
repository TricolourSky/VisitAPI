using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT.CameraControl;
using HarmonyLib;
using UnityEngine;

namespace VisitAPI.Native;

// ══ 「CameraManager 状态不完整时别炸」一组（旧版 4 个文件，主题相同收成一处）══
// 菜单/访问切换期间 Camera、后处理 profile、_ssaaImpl 都可能是空——
// 缺哪样就安全跳过哪条原生路径，等相机链就位后一切照旧。

[HarmonyPatch]
public static class CameraSafety
{
    [HarmonyPrefix, HarmonyPatch(typeof(CameraManager), "IsActive", MethodType.Setter)]
    static bool SetIsActive(CameraManager __instance)
    {
        if (__instance.Camera != null) return true;
        Plugin.Log.LogDebug("[narrate] IsActive ignored - no camera bound");
        return false;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(CameraManager), "IsActive", MethodType.Getter)]
    static bool GetIsActive(CameraManager __instance, ref bool __result)
    {
        if (__instance.Camera != null) return true;
        __result = false;
        return false;
    }

    // AmbientLight.Initialize() 每帧问 SSR；相机或后处理 profile 缺失时直接答「没开」让它往下走
    [HarmonyPrefix, HarmonyPatch(typeof(CameraManager), nameof(CameraManager.GetSSREnabled))]
    static bool Ssr(CameraManager __instance, ref bool __result)
    {
        if (__instance.Camera != null && __instance._postProcessVolume != null && __instance._postProcessVolume.profile != null) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch]
public static class UpscalerGuard
{
    static readonly FieldInfo SsaaImplField = AccessTools.Field(typeof(CameraManager), "_ssaaImpl");

    // 某个方法名在本 build 不存在时只丢那一条，不让整组挂载失败（09-07 终审）
    static IEnumerable<MethodBase> TargetMethods() => new MethodBase[]
    {
        AccessTools.Method(typeof(CameraManager), "SetFSR"),
        AccessTools.Method(typeof(CameraManager), "SetFSR2"),
        AccessTools.Method(typeof(CameraManager), "SetFSR3"),
        AccessTools.Method(typeof(CameraManager), "SetDLSSPreset"),
    }.Where(m => m != null);

    static bool Prefix(CameraManager __instance)
    {
        if (SsaaImplField == null || SsaaImplField.GetValue(__instance) as Object != null) return true;
        Plugin.Log.LogDebug("[narrate] camera has no SSAAImpl - upscaler setup skipped");
        return false;
    }
}
