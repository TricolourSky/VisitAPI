using System;
using System.Reflection;
using HarmonyLib;

namespace VisitAPI.Native
{
    internal static class WhitelistPatch
    {
        private static readonly string[] VanillaTraders =
        {
            "638f541a29ffd1183d187f57",
            "656f0f98d80a697f855d34b1",
            "54cb50c76803fa8b248b4571",
            "54cb57776803fa99248b456e",
        };

        internal static void Apply(Harmony harmony)
        {
            if (NativeBinder.Method5 == null)
            {
                Plugin.Log.LogWarning("[WhitelistPatch] method_5 not bound; custom traders will hit the vanilla whitelist");
                return;
            }
            MethodInfo finalizer = typeof(WhitelistPatch).GetMethod(nameof(Method5Finalizer), BindingFlags.Static | BindingFlags.NonPublic);
            harmony.Patch(NativeBinder.Method5, finalizer: new HarmonyMethod(finalizer));
            Plugin.Log.LogInfo("[WhitelistPatch] method_5 finalizer installed");
        }

        private static Exception? Method5Finalizer(Exception __exception, object __instance)
        {
            if (__exception == null) return null;
            string? traderId = NativeBinder.GetScreenTraderId(__instance);
            if (traderId != null && Plugin.RegisteredTraders.Contains(traderId) && Array.IndexOf(VanillaTraders, traderId) < 0)
            {
                Plugin.Log.LogInfo("[WhitelistPatch] suppressed whitelist throw for custom trader " + traderId);
                return null;
            }
            return __exception;
        }
    }
}
