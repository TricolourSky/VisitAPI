using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VisitAPI.Native
{
    internal static class OptionRowPatch
    {
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal static void Apply(Harmony harmony)
        {
            if (DialogUiBinder.RowType == null)
            {
                Plugin.Log.LogWarning("[OptionRowPatch] OptionRow type not bound; skipping");
                return;
            }
            Patch(harmony, "OnPointerClick", nameof(ClickPrefix));
            Patch(harmony, "OnPointerEnter", nameof(EnterPrefix));
            Patch(harmony, "OnPointerExit", nameof(ExitPrefix));
            Plugin.Log.LogInfo("[OptionRowPatch] option-row click/hover patched");
        }

        private static void Patch(Harmony harmony, string method, string prefix)
        {
            MethodInfo target = DialogUiBinder.RowType.GetMethods(All)
                .FirstOrDefault(m => m.Name == method && m.GetParameters().Length == 1);
            if (target == null)
            {
                Plugin.Log.LogWarning("[OptionRowPatch] " + method + " not found");
                return;
            }
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(OptionRowPatch).GetMethod(prefix, BindingFlags.Static | BindingFlags.NonPublic)));
        }

        private static bool ClickPrefix(object __instance)
        {
            VisitOptionRow marker = (__instance as Component)?.GetComponent<VisitOptionRow>();
            if (marker == null) return true;
            marker.Fire();
            return false;
        }

        private static bool EnterPrefix(object __instance)
        {
            if ((__instance as Component)?.GetComponent<VisitOptionRow>() == null) return true;
            DialogUiBinder.SetRowHighlight(__instance, true);
            return false;
        }

        private static bool ExitPrefix(object __instance)
        {
            if ((__instance as Component)?.GetComponent<VisitOptionRow>() == null) return true;
            DialogUiBinder.SetRowHighlight(__instance, false);
            return false;
        }
    }
}
