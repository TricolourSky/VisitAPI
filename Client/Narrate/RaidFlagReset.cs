using System;
using HarmonyLib;

namespace VisitAPI.Native;

public static class RaidFlagReset
{
    public static void Run()
    {
        Reset("MoxoPixel.MenuOverhaul.Utils.GameStateUtility", "ResetGameState", "WTT-MenuOverhaul");
    }

    static void Reset(string typeName, string method, string label)
    {
        try
        {
            var t = AccessTools.TypeByName(typeName);
            if (t == null) return;
            var m = AccessTools.Method(t, method);
            if (m == null) { Plugin.Log.LogWarning($"[narrate] {label} 已装但找不到 {typeName}.{method}（版本变了？），战局标记没复位"); return; }
            m.Invoke(null, null);
            Plugin.Log.LogInfo($"[narrate] {label} 的战局标记已复位（访问期它把自己挂起了）");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"[narrate] {label} 战局标记复位失败: " + e.Message); }
    }
}
