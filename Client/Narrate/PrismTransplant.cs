using System;
using System.Reflection;
using EFT.EnvironmentEffect;
using HarmonyLib;
using UnityEngine;

namespace VisitAPI.Native;

public static class PrismTransplant
{
    public static bool Pinned;
    public static float MiddleGrey;
    public static float Speed;
    public static Component Prefab;

    public static bool Apply(Component sourcePrefab, Camera camera)
    {
        Prefab = sourcePrefab;
        var from = sourcePrefab != null ? sourcePrefab.GetComponent<PrismEffects>() : null;
        var to = camera != null ? camera.GetComponentInChildren<PrismEffects>(true) : null;
        if (from == null || to == null)
        {
            Pinned = false;
            Plugin.Log.LogWarning($"[narrate] Prism 原生参数未搬：包内相机Prism={from != null} 本机相机Prism={to != null}");
            return false;
        }
        var copied = Reflect.Copy(from, to, typeof(PrismEffects), out var kept);
        var shaders = Shaders11(from, to);
        MiddleGrey = to.exposureMiddleGrey;
        Speed = to.exposureSpeed;
        Pinned = true;
        Plugin.Log.LogInfo($"[narrate] Prism 原生参数已搬 {copied} 项（保留本机引用 {kept}）: "
            + $"曝光={to.useExposure}(灰度{to.exposureMiddleGrey:0.###} 上限{to.exposureUpperLimit:0.##}) "
            + $"色调映射={to.useTonemap}({to.tonemapType}) 暗角={to.useVignette}({to.vignetteStrength:0.###}) 泛光={to.useBloom}；"
            + $"EnvironmentManager 覆写源: {Override()}；shader 换成包内 1.1 版: {shaders}");
        return true;
    }

    static string Shaders11(PrismEffects from, PrismEffects to)
    {
        var used = new System.Collections.Generic.List<string>();
        if (Swap(from.m_Shader, ref to.m_Shader, ref to.m_Material)) used.Add(to.m_Shader.name);
        if (Swap(from.m_Shader2, ref to.m_Shader2, ref to.m_Material2)) used.Add(to.m_Shader2.name);
        if (Swap(from.m_Shader3, ref to.m_Shader3, ref to.m_Material3)) used.Add(to.m_Shader3.name);
        if (Swap(from.m_AOShader, ref to.m_AOShader, ref to.m_AOMaterial)) used.Add(to.m_AOShader.name);
        return used.Count > 0 ? string.Join(", ", used) : "无（包内没有可用的）";
    }

    static bool Swap(Shader src, ref Shader dst, ref Material mat)
    {
        if (src == null || !src.isSupported || src == dst) return false;
        dst = src;
        if (mat != null) { UnityEngine.Object.Destroy(mat); mat = null; }
        return true;
    }

    static string Override()
    {
        var env = EnvironmentManager.Instance;
        if (env == null) return "无（Prism 用自己的值）";
        return $"{env.name} 环境={env.Environment} 灰度={env.PrismExposureOffset:0.###} 速度={env.PrismExposureSpeed:0.##}（访问期钉成 {MiddleGrey:0.###}/{Speed:0.##}）";
    }
}

[HarmonyPatch(typeof(EnvironmentManager), nameof(EnvironmentManager.Update))]
public static class PrismExposurePin
{
    static void Postfix(EnvironmentManager __instance)
    {
        if (!PrismTransplant.Pinned || !Narrating.Now) return;
        __instance.PrismExposureOffset = PrismTransplant.MiddleGrey;
        __instance.PrismExposureSpeed = PrismTransplant.Speed;
    }
}
