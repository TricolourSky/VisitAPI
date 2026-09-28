using System;
using System.Linq;
using Comfort.Common;
using EFT.CameraControl;
using EFT.Settings;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace VisitAPI.Native;

public static class ReflectionPin
{
    public static void Apply(Camera camera)
    {
        var cm = CameraManager.Instance;
        if (cm == null || cm.Camera == null || camera == null) return;
        try
        {
            var volume = camera.GetComponent<PostProcessVolume>();
            if (volume != null) OnlySsr(volume);
            if (Singleton<SettingsManager>.Instantiated) cm.SetSSR(Singleton<SettingsManager>.Instance.Graphics.Settings.SSR.Value);
            Refresh();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[narrate] Reflection setup failed: " + e.GetType().Name + " " + e.Message);
        }
    }

    static void OnlySsr(PostProcessVolume volume)
    {
        volume.enabled = true;
        foreach (var setting in volume.profile.settings)
            setting.active = setting is ScreenSpaceReflections;
    }

    static void Refresh()
    {
        foreach (var al in UnityEngine.Object.FindObjectsOfType<AmbientLight>())
            al.SetReflectionIntensity(al.ReflectionIntensity);
    }

    [HarmonyPatch]
    public static class AuthoredIntensity
    {
        static System.Reflection.MethodBase TargetMethod() => AccessTools.Method(typeof(AmbientLight), "method_3");

        static readonly System.Reflection.FieldInfo BlockField = AccessTools.Field(typeof(AmbientLight), "_screenAmbientBlock");
        static readonly int ReflectionIntensityId = Shader.PropertyToID("_ReflectionIntensity");

        static void Postfix(AmbientLight __instance)
        {
            if (!Narrating.Now || BlockField == null) return;
            if (BlockField.GetValue(__instance) is MaterialPropertyBlock block) block.SetFloat(ReflectionIntensityId, __instance.ReflectionIntensity);
        }
    }
}
