using System;
using System.Reflection;
using HarmonyLib;

namespace VisitAPI.Native
{
    internal static class FavoriteSchemeGuard
    {
        private static bool _logged;

        internal static void Apply(Harmony harmony)
        {
            Type type = AccessTools.TypeByName("PlayerPrefHelperClass");
            MethodInfo method = type?.GetMethod("TryGetFavoriteIndex",
                BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
            {
                Plugin.Log.LogWarning("[FavoriteSchemeGuard] PlayerPrefHelperClass.TryGetFavoriteIndex not found; guard inactive");
                return;
            }
            harmony.Patch(method, finalizer: new HarmonyMethod(typeof(FavoriteSchemeGuard).GetMethod(nameof(Finalizer), BindingFlags.Static | BindingFlags.NonPublic)));
            Plugin.Log.LogInfo("[FavoriteSchemeGuard] installed (TryGetFavoriteIndex)");
        }

        private static Exception? Finalizer(Exception __exception, ref int index, ref bool __result)
        {
            if (__exception == null) return null;
            index = -1;
            __result = false;
            if (!_logged)
            {
                _logged = true;
                Plugin.Log.LogWarning("[FavoriteSchemeGuard] swallowed " + __exception.GetType().Name + " in TryGetFavoriteIndex; hideout favorites treated as empty");
            }
            return null;
        }
    }
}
