using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace VisitAPI.Native;

public static class AmbientGuard
{
    static readonly string[] Registries =
    {
        "AnalyticSources", "AnalyticSourcesSorted",
        "DynamicAnalyticSources", "DynamicAnalyticSourcesSorted",
        "StencilShadowsObjects", "StencilShadowsObjectsSorted",
        "_runtimeStaticSources",
    };

    internal static void Clear()
    {
        var purged = 0;
        foreach (var name in Registries)
        {
            var field = AccessTools.Field(typeof(AmbientLight), name);
            if (!(field?.GetValue(null) is IEnumerable col)) continue;
            var alive = new List<object>();
            var dead = 0;
            foreach (var o in col)
            {
                if (o is Object u && u != null) alive.Add(o);
                else dead++;
            }
            if (dead == 0) continue;
            purged += dead;
            var t = col.GetType();
            t.GetMethod("Clear")?.Invoke(col, null);
            var add = t.GetMethod("Add");
            if (add != null) foreach (var o in alive) add.Invoke(col, new[] { o });
        }
        if (purged > 0) Plugin.Log.LogInfo($"[narrate] ambient light registries purged: {purged} destroyed source(s)");
    }
}

[HarmonyPatch]
public static class AmbientDrawGuard
{
    static readonly System.Reflection.FieldInfo ListField = AccessTools.Field(typeof(AmbientLight), "_runtimeStaticSources");
    static int _logged;
    static bool _nullLogged;

    internal static void RebuildSnapshot()
    {
        AmbientLight.RuntimeOptimizePrepare();
        _nullLogged = false;
        var count = (ListField?.GetValue(null) as System.Collections.ICollection)?.Count ?? -1;
        Plugin.Log.LogInfo($"[narrate] ambient static sources prepared: {count}");
    }

    [HarmonyPrefix, HarmonyPatch(typeof(AmbientLight), nameof(AmbientLight.RuntimeDrawStaticSourcesOptimized))]
    static bool DrawList()
    {
        if (ListField == null || ListField.GetValue(null) != null) return true;
        if (!_nullLogged) { _nullLogged = true; Plugin.Log.LogWarning("[narrate] ambient snapshot list is null - draw skipped until next Prepare"); }
        return false;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(AmbientLight), nameof(AmbientLight.DrawAnalyticSourceOptimized))]
    static bool Optimized(AnalyticSource source) => Valid(source);

    [HarmonyPrefix, HarmonyPatch(typeof(AmbientLight), nameof(AmbientLight.DrawAnalyticSource))]
    static bool Plain(AnalyticSource source) => Valid(source);

    static bool Valid(AnalyticSource source)
    {
        if (source != null && source.Culling != null && source.MaterialPropertyBlock != null) return true;
        if (_logged < 8)
        {
            _logged++;
            var desc = source == null
                ? "destroyed/null source"
                : $"'{source.name}' scene={source.gameObject.scene.name} culling={(source.Culling == null ? "NULL" : "ok")} mpb={(source.MaterialPropertyBlock == null ? "NULL" : "ok")}";
            Plugin.Log.LogWarning("[narrate] ambient analytic source skipped: " + desc);
        }
        return false;
    }
}
