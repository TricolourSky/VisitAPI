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
            if (m == null) { Plugin.Log.LogWarning($"[narrate] {label} is installed but {typeName}.{method} not found (version changed?), raid flag not reset"); return; }
            m.Invoke(null, null);
        }
        catch (Exception e) { Plugin.Log.LogWarning($"[narrate] {label} raid flag reset failed: " + e.Message); }
    }
}
