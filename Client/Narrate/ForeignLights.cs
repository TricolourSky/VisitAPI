using System.Collections.Generic;
using EFT;
using UnityEngine;

namespace VisitAPI.Native;

public static class ForeignLights
{
    static readonly List<Light> _muted = new();

    /// 只关开着的灯，记下来由 Restore 重新打开。09-24 审查低项：以前开头先清列表，连续 Mute 两次时第一批灯已经关了、
    /// 第二次不会再记到，就再也恢复不了；现在只往列表里追加
    public static void Mute()
    {
        foreach (var l in Object.FindObjectsOfType<Light>())
        {
            if (!l.enabled || !l.gameObject.activeInHierarchy) continue;
            if (l.GetComponentInParent<Player>() == null) continue;
            l.enabled = false;
            if (!_muted.Contains(l)) _muted.Add(l);
        }
    }

    public static void Restore()
    {
        foreach (var l in _muted)
            if (l != null) l.enabled = true;
        _muted.Clear();
    }
}
