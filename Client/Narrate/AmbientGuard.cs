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

    // 七个登记表的字段和各自集合类型的 Clear / Add 只反射一次
    static System.Reflection.FieldInfo[] _fields;
    static readonly Dictionary<System.Type, (System.Reflection.MethodInfo clear, System.Reflection.MethodInfo add)> _ops = new();

    internal static void Clear()
    {
        _fields ??= System.Array.ConvertAll(Registries, name => AccessTools.Field(typeof(AmbientLight), name));
        foreach (var field in _fields)
        {
            if (!(field?.GetValue(null) is IEnumerable col)) continue;
            var alive = new List<object>();
            var dead = 0;
            foreach (var o in col)
            {
                if (o is Object u && u != null) alive.Add(o);
                else dead++;
            }
            if (dead == 0) continue;
            var t = col.GetType();
            if (!_ops.TryGetValue(t, out var ops)) _ops[t] = ops = (t.GetMethod("Clear"), t.GetMethod("Add"));
            ops.clear?.Invoke(col, null);
            if (ops.add != null) foreach (var o in alive) ops.add.Invoke(col, new[] { o });
        }
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
