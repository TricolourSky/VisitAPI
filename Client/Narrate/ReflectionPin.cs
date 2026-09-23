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
            var kept = volume != null ? OnlySsr(volume) : "无 PostProcessVolume";
            if (Singleton<SettingsManager>.Instantiated) cm.SetSSR(Singleton<SettingsManager>.Instance.Graphics.Settings.SSR.Value);
            Refresh();
            Plugin.Log.LogInfo($"[narrate] 反射: PPv2 只留 SSR（{kept}），SSR={cm.GetSSREnabled()}，AmbientLight[{Describe()}]");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[narrate] 反射设置失败: " + e.GetType().Name + " " + e.Message);
        }
    }

    static string OnlySsr(PostProcessVolume volume)
    {
        volume.enabled = true;
        var off = 0;
        foreach (var setting in volume.profile.settings)
        {
            var keep = setting is ScreenSpaceReflections;
            if (!keep && setting.active) off++;
            setting.active = keep;
        }
        return $"关掉其余 {off} 项";
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
            if (!Narrating.Now || !Plugin.AmbientReflection.Value || BlockField == null) return;
            if (BlockField.GetValue(__instance) is MaterialPropertyBlock block) block.SetFloat(ReflectionIntensityId, __instance.ReflectionIntensity);
        }
    }

    static string Describe() =>
        string.Join("; ", UnityEngine.Object.FindObjectsOfType<AmbientLight>().Select(a =>
        {
            var block = Traverse.Create(a).Field("_screenAmbientBlock").GetValue<MaterialPropertyBlock>();
            return $"{a.gameObject.scene.name} SSR判定={a.IsSSREnable} 块内={(block != null ? block.GetFloat("_ReflectionIntensity") : -1f):0.###}";
        }));
}
