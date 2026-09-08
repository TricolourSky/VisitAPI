using System;
using Comfort.Common;
using EFT;
using UnityEngine;
using VisitAPI.Dialog;

namespace VisitAPI.Native;

/// <summary>
/// 世界内触发点的生成器：进图后一次性把匹配当前地图的 `trigger:` 生成成 GameObject。
/// 与 1.2.1 的差别（T-1）：按 **GameWorld 实例变化**判定「进了新图」，
/// 不再依赖 1 秒轮询恰好撞见「世界不存在的那一帧」——错过窗口=新图零触发点的隐患从根上消除。
/// </summary>
public static class TriggerHost
{
    static float _next;
    static GameWorld _spawnedFor;   // 已为哪个世界实例生成过
    static int _live;

    public static void Tick()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 1f;
        if (!Singleton<GameWorld>.Instantiated) { _spawnedFor = null; return; }
        var world = Singleton<GameWorld>.Instance;
        if (world is NarrateGameWorld || ReferenceEquals(world, _spawnedFor)) return;
        var locationId = world.LocationId ?? "";
        if (locationId.Length == 0) return;   // 世界还没就绪，下秒再看（不标记，绝不漏）
        _spawnedFor = world;
        _live = 0;
        var inHideout = Raid.IsHideout(locationId);
        foreach (var tree in DialogFiles.All())
            foreach (var trigger in tree.Triggers)
            {
                var matches = inHideout
                    ? trigger.Kind == "hideout"
                    : trigger.Kind == "raid" && MapMatches(trigger.Place, locationId);
                if (matches) Spawn(tree.TraderId, trigger, inHideout);
            }
        Plugin.Log.LogInfo($"[trigger] {locationId}: 生成 {_live} 个触发点");
    }

    static void Spawn(string traderId, DialogTrigger tr, bool hideout)
    {
        var go = new GameObject("VisitTrigger_" + traderId);
        var t = go.AddComponent<VisitTrigger>();
        t.TraderId = traderId;
        t.Data = tr;
        t.Merge = hideout && !tr.Free;
        t.RequireLook = (!hideout || tr.Free) && !t.Auto;
        _live++;
    }

    static bool MapMatches(string place, string loc) =>
        place == "*" || loc.IndexOf(place, StringComparison.OrdinalIgnoreCase) >= 0 || place.IndexOf(loc, StringComparison.OrdinalIgnoreCase) >= 0;
}
