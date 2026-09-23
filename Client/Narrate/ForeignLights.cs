using System.Collections.Generic;
using System.Linq;
using EFT;
using UnityEngine;

namespace VisitAPI.Native;

public static class ForeignLights
{
    static readonly List<Light> _muted = new();

    public static void Mute()
    {
        _muted.Clear();
        foreach (var l in Object.FindObjectsOfType<Light>())
        {
            if (!l.enabled || !l.gameObject.activeInHierarchy) continue;
            if (l.GetComponentInParent<Player>() == null) continue;
            l.enabled = false;
            _muted.Add(l);
        }
        if (_muted.Count == 0) { Plugin.Log.LogInfo("[narrate] 玩家身上没有开着的灯"); return; }
        Plugin.Log.LogInfo($"[narrate] 关掉玩家身上的灯 {_muted.Count} 盏: "
            + string.Join("; ", _muted.Select(l => $"{l.name}({l.type} {l.intensity:0.##}/{l.range:0.#}m)")));
    }

    public static void Restore()
    {
        var back = 0;
        foreach (var l in _muted)
            if (l != null) { l.enabled = true; back++; }
        if (_muted.Count > 0) Plugin.Log.LogInfo($"[narrate] 玩家身上的灯恢复 {back}/{_muted.Count}");
        _muted.Clear();
    }
}
