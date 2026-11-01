using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace VisitAPI.Native;

/// 访问收尾时把别的菜单模组记的「在战局里」标记复位（WTT-MenuOverhaul 把访问当成进图了，回主菜单后界面不对）
public static class RaidFlagReset
{
    // 按名字找类型要扫一遍全部程序集：找过一次就记住，「没装 / 没找到」的 null 也记住
    static readonly Dictionary<string, MethodInfo> _found = new(StringComparer.Ordinal);

    public static void Run()
    {
        Reset("MoxoPixel.MenuOverhaul.Utils.GameStateUtility", "ResetGameState", "WTT-MenuOverhaul");
    }

    static void Reset(string typeName, string method, string label)
    {
        try
        {
            var key = typeName + "." + method;
            if (!_found.TryGetValue(key, out var m))
            {
                var t = AccessTools.TypeByName(typeName);
                m = t == null ? null : AccessTools.Method(t, method);
                if (t != null && m == null) Plugin.Log.LogWarning($"[narrate] {label} is installed but {typeName}.{method} not found (version changed?), raid flag not reset");
                _found[key] = m;
            }
            m?.Invoke(null, null);
        }
        catch (Exception e) { Plugin.Log.LogWarning($"[narrate] {label} raid flag reset failed: " + e.Message); }
    }
}
