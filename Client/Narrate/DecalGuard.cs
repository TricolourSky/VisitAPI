using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VisitAPI.Native;

/// <summary>
/// 材质/shader 已失效的贴花不让注册/反注册（成组 NRE 的来源，M4m）；
/// 退出访问前清空贴花缓存并重置计数/脏标志，避免二次进入时 OnDisable 链爆炸。
/// </summary>
[HarmonyPatch]
public static class DecalGuard
{
    static readonly FieldInfo CountField = AccessTools.Field(typeof(StaticDeferredDecalRenderer), "_allDecalsCount");
    static readonly FieldInfo DirtyField = AccessTools.Field(typeof(StaticDeferredDecalRenderer), "_decalBuffersDirty");
    static int _skipped;

    [HarmonyPatch(typeof(StaticDeferredDecalRenderer), nameof(StaticDeferredDecalRenderer.RegisterDecal))]
    [HarmonyPrefix]
    static bool RegisterPrefix(StaticDeferredDecal __0)
    {
        if (__0 != null) SceneShaders.FixDecal(__0.DecalMaterial);   // 必须在注册前：管理器一注册就 new Material 复制走 shader（坑 #113）
        return Valid(__0) || Skip();
    }

    [HarmonyPatch(typeof(StaticDeferredDecalRenderer), nameof(StaticDeferredDecalRenderer.UnregisterDecal))]
    [HarmonyPrefix]
    static bool UnregisterPrefix(StaticDeferredDecal __0) => Valid(__0) || Skip();

    internal static void Clear()
    {
        var renderer = StaticDeferredDecalRenderer.Instance;
        if (renderer != null)
        {
            renderer.ClearDecals();
            CountField?.SetValue(renderer, 0);
            DirtyField?.SetValue(renderer, true);
        }
        Plugin.Log.LogDebug($"[narrate] static decal cache cleared; invalid events skipped={_skipped}");
        _skipped = 0;
    }

    static bool Valid(StaticDeferredDecal decal)
    {
        if (decal == null) return false;
        var material = decal.DecalMaterial;
        // 主贴图也必须在：贴图丢失的贴花会渲成白印（2026-09-03 天花板白纹嫌疑；箱面喷字消失=同病另一面）
        return material != null && material.shader != null && material.mainTexture != null;
    }

    static int _outsideLogged;

    // 这条补丁不分访问内外（Register 发生在场景加载中，早于任何「访问中」判据就位）。访问以外被拒的贴花以前是静默的——
    // 09-07 终审：至少把前几条打出来，战局/藏身处若有贴花因 _MainTex 为空而消失，日志里能看见（有限 5 条）
    static bool Skip()
    {
        _skipped++;
        if (!Narrating.Now && _outsideLogged < 5) { _outsideLogged++; Plugin.Log.LogWarning($"[narrate] 访问以外拒绝了一个材质/shader/主贴图缺失的贴花（第 {_outsideLogged} 次记录，之后不再记）"); }
        return false;
    }
}
