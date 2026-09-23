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
            Plugin.Log.LogInfo($"[trader] 初始商人 {nextSelected.Id} 锁着，改选第一个可用的 {first.Id}");
            nextSelected = first;
        }
        catch (Exception e) { Plugin.Log.LogWarning("[trader] 初始商人改选失败（照原生）: " + e.Message); }
    }
}
