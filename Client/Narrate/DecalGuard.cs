using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VisitAPI.Native;

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
        if (__0 != null) SceneShaders.FixDecal(__0.DecalMaterial);
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
        return material != null && material.shader != null && material.mainTexture != null;
    }

    static int _outsideLogged;

    static bool Skip()
    {
        _skipped++;
        if (!Narrating.Now && _outsideLogged < 5) { _outsideLogged++; Plugin.Log.LogWarning($"[narrate] 访问以外拒绝了一个材质/shader/主贴图缺失的贴花（第 {_outsideLogged} 次记录，之后不再记）"); }
        return false;
    }
}
