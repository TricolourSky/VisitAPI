using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT.CameraControl;
using HarmonyLib;
using UnityEngine;

namespace VisitAPI.Native;

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
