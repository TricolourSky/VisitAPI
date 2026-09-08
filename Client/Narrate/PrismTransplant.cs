using System;
using System.Reflection;
using EFT.EnvironmentEffect;
using HarmonyLib;
using UnityEngine;

namespace VisitAPI.Native;

/// <summary>
/// 原生模式的相机后处理：1.1 商人房相机预制体（LevelSettings.CameraPrefab，包里原样带着）上的 PrismEffects
/// 就是 1.1 实际渲染用的曝光 / 色调映射 / 暗角 / 镜头脏参数（09-03 从 native6 包里逐字段核过）。
/// 相机本体仍用 0.16 的 Cam2_fps_hideout（1.1 相机预制体在 0.16 只有 6 个组件能绑上，整搬黑屏），
/// 只把这一个组件的序列化字段搬过来——数值全是包内原生数据，插件不自己发明参数。
/// Shader / Material / Transform / 预设引用继续用 0.16 自己的；贴图（镜头脏、噪声、LUT）照搬。
/// </summary>
public static class PrismTransplant
{
    /// <summary>搬过来的曝光灰度 / 速度——供 <see cref="PrismExposurePin"/> 每帧钉回去。</summary>
    public static bool Pinned;
    public static float MiddleGrey;
    public static float Speed;
    /// <summary>包内 1.1 相机预制体——<see cref="Camera11"/> 从它上面再搬别的组件（散射的抖动贴图等）。</summary>
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

    /// <summary>包里带着 1.1 自己的 Prism shader（Hidden/PrismEffects 1.1 版 166360 字节，0.16 那份 163652 字节，两代不是同一份码），
    /// 曝光 / 色调映射 / 暗角就在这几张 shader 里算。之前只搬参数、shader 仍用 0.16 的；改成用包内 1.1 的，
    /// 对应材质销毁置空，PrismEffects 下一帧 CreateMaterials 按新 shader 重建。本机不支持的留本机的。</summary>
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

    // 「照搬序列化字段」的实现 2026-09-05 收进 Core\Reflect.Copy（原来这里和 Camera11 各写了一套）。
}

/// <summary>
/// 0.16 的 `PrismEffects.OnRenderImage` 每帧把 `EnvironmentManager.Instance.PrismExposureOffset / Speed` 灌进
/// `exposureMiddleGrey / exposureSpeed`——而 EnvironmentManager 那两个值来自藏身处（户外 0.23 或藏身处室内触发器），
/// 1.1 的 EnvironmentManager 根本没有曝光字段，1.1 的 Prism 用的是相机预制体上自己的 0.12。
/// 这就是「参数搬对了画面照样亮一截」的根（09-03 深夜）。访问期在它 Update 之后把两个值钉成搬来的 1.1 值，
/// PrismEffects 再读就是 1.1 的；退出访问后不再干预。
/// </summary>
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
