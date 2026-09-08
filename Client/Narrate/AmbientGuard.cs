using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace VisitAPI.Native;

/// <summary>
/// AmbientLight 与贴花渲染器同款病：静态注册表（AnalyticSources 等 6 张 + 快照 _runtimeStaticSources）
/// 在场景卸载后残留死对象（SortedSet 按优先级排序，优先级变过 Remove 就会失手），
/// 二次进入后 RuntimeDrawStaticSourcesOptimized 每帧 NRE（实机 5725 次/次访问）。
/// 清法：把已销毁（Unity 假 null）的条目从各表剔除，活的原样保留——战局/藏身处的光源不受影响。
/// </summary>
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

/// <summary>
/// 二次进入 NRE 风暴的**真根因**（四轮实机 + 反编译死证）：`RuntimeOptimizeClear()` 在 AmbientLight
/// OnDestroy 时把静态快照列表置 null（AmbientLight.cs:851），而重建入口 `RuntimeOptimizePrepare()`
/// 全游戏只有战局加载流程调一次（TarkovApplication.cs:2778）——访问路径没人调，第二次进入就是遍历 null。
/// 修法=进场景后补调引擎自己的 Prepare（顺带让环境光源在访问中真正被绘制）；下面的守卫只是保险。
/// </summary>
[HarmonyPatch]
public static class AmbientDrawGuard
{
    static readonly System.Reflection.FieldInfo ListField = AccessTools.Field(typeof(AmbientLight), "_runtimeStaticSources");
    static int _logged;
    static bool _nullLogged;

    /// <summary>进场后重建光源快照（对齐战局加载流程 TarkovApplication.cs:2778 那一句）。
    /// ⚠️ 不能叫 Prepare/Cleanup/TargetMethod——补丁类里这些名字是 Harmony 的保留钩子，会被自动调用（坑 #96）。</summary>
    internal static void RebuildSnapshot()
    {
        AmbientLight.RuntimeOptimizePrepare();
        _nullLogged = false;
        var count = (ListField?.GetValue(null) as System.Collections.ICollection)?.Count ?? -1;
        Plugin.Log.LogInfo($"[narrate] ambient static sources prepared: {count}");
    }

    // 保险：快照列表为 null（AmbientLight 被销毁过、Prepare 还没跑到）时跳过整段绘制，别再每帧炸
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
