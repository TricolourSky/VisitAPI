using System;
using System.Linq;
using Comfort.Common;
using EFT.CameraControl;
using EFT.Settings;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace VisitAPI.Native;

/// <summary>
/// 光泽面（箱子 / 货架 / 台灯罩，全是带 `_Cube` 的 Reflective 材质）发白：
/// 09-03 深夜实测把 SSR 关掉、AmbientLight 反射强度落到 0.2，画面纹丝不动——说明发白不是环境反射强度，
/// 而是 p0 Reflective 系 shader 在「SSR 开」时把立方图反射交给屏幕反射去盖，SSR 没画就只剩满强度的立方图。
/// 1.1 场景数据 `LevelSettings.SSRFactor=1`、AmbientLight `ReflectionIntensitySSR=1`，1.1 的截图是 SSR 开着的样子。
/// 0.16 的 SSR 在 PPv2 的 profile 里（我们按 1.1 名单关掉了整个 PostProcessVolume 才把它一起关了）：
/// 这里把 Volume 开回来、但只留 ScreenSpaceReflections 一项（其余藏身处调色仍关），SSR 跟玩家画质设置走。
/// profile 是 PPv2 给每台相机的运行时副本，随相机销毁，不污染藏身处。
/// </summary>
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
            al.SetReflectionIntensity(al.ReflectionIntensity);   // 重新走一遍 IsSSREnable ? 1 : 0.2×曲线
    }

    /// <summary>
    /// 屏幕环境光那一遍（AmbientLight 的 "Custom Ambient" 缓冲，全屏一张 `Hidden/WriteScreenAmbient`）按
    /// `_ReflectionIntensity` 给全画面加一层环境光+反射。0.16 的取值在 `method_3`：**SSR 开着就一律取 1**，
    /// 否则才取场景写的 0.2。这里在它每帧算完之后按回字段值 0.2。
    ///
    /// 09-05 一度以为这条补丁「违背 1.1」而删掉——1.1 的 ambientManager 上确实是两个字段
    /// （`ReflectionIntensity: 0.2` 和 `ReflectionIntensitySSR: 1`），0.16 的取法看着与 1.1 一致。
    /// 但实机一对比，删掉后全画面亮一档、平一档（坑 #124）。**字面一致 ≠ 跑出来一致**：这一遍垫上去的
    /// 底光来自那张按白天蓝空烤的反射立方图（见 `SceneLighting.DimReflection`），乘 1 就是垫 5 倍。
    /// 判据以实机为准，别再按字面值删。
    /// </summary>
    [HarmonyPatch]
    public static class AuthoredIntensity
    {
        static System.Reflection.MethodBase TargetMethod() => AccessTools.Method(typeof(AmbientLight), "method_3");

        static void Postfix(AmbientLight __instance)
        {
            if (!Narrating.Now || !Plugin.AmbientReflection.Value) return;
            var block = Traverse.Create(__instance).Field("_screenAmbientBlock").GetValue<MaterialPropertyBlock>();
            block?.SetFloat("_ReflectionIntensity", __instance.ReflectionIntensity);
        }
    }

    static string Describe() =>
        string.Join("; ", UnityEngine.Object.FindObjectsOfType<AmbientLight>().Select(a =>
        {
            var block = Traverse.Create(a).Field("_screenAmbientBlock").GetValue<MaterialPropertyBlock>();
            return $"{a.gameObject.scene.name} SSR判定={a.IsSSREnable} 块内={(block != null ? block.GetFloat("_ReflectionIntensity") : -1f):0.###}";
        }));
}
