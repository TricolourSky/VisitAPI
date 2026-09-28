using System;
using System.Linq;
using EFT.Trading;
using EFT.UI;
using HarmonyLib;

namespace VisitAPI.Native;

[HarmonyPatch(typeof(TraderScreensGroup), nameof(TraderScreensGroup.SelectTrader))]
public static class LockedTraderSelect
{
    static void Prefix(TraderScreensGroup __instance, ref Trader nextSelected)
    {
        try
        {
            if (nextSelected?.Info == null || nextSelected.Info.Available || __instance.Trader != nextSelected) return;
            var first = __instance.TradersList?.FirstOrDefault(t => t?.Info != null && t.Info.Available);
            if (first == null) return;
            nextSelected = first;
        }
        catch (Exception e) { Plugin.Log.LogWarning("[trader] Failed to reselect initial trader (keeping native choice): " + e.Message); }
    }
}
