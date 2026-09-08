using System.Collections.Generic;
using System.Linq;
using EFT;
using UnityEngine;

namespace VisitAPI.Native;

/// <summary>
/// 房间以外的灯：全局清点（09-03 深夜）抓到玩家角色身上那把枪的战术手电（Insight WMX200，白光 1.7 / 射程 55m）
/// 在访问期开着——人就坐在房间里，等于一束正面平光把箱子 / 货架 / 台灯罩全打白，1.1 里没有这盏灯。
/// 访问期把挂在玩家层级下的 Light 全关掉，退出访问按原状恢复。
/// </summary>
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
