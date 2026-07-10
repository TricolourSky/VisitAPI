using System;
using System.Reflection;
using EFT.UI;
using HarmonyLib;
using UnityEngine;

namespace VisitAPI.Native
{
    // The single out-of-raid entry. Postfix on TraderScreensGroup.method_6 (per-trader select, also runs on
    // first Show): clone the native close button into ONE visit button, shown for a registered trader with a
    // `.dlg` (对话 — opens the VisitAPI dialog with the controllers the trade screen already holds; hidden while
    // the .dlg's `tab:` quest-gate is closed). Hidden for everyone else.
    internal static class TraderScreenEntryPatch
    {
        internal static void Apply(Harmony harmony)
        {
            if (NativeBinder.TsgMethod6 == null)
            {
                Plugin.Log.LogWarning("[TalkButton] TraderScreensGroup.method_6 not bound; out-of-raid Talk button disabled");
                return;
            }
            MethodInfo postfix = typeof(TraderScreenEntryPatch).GetMethod(nameof(Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            harmony.Patch(NativeBinder.TsgMethod6, postfix: new HarmonyMethod(postfix));
            Plugin.Log.LogInfo("[TalkButton] entry patch installed on TraderScreensGroup.method_6");
        }

        private static void Postfix(object __instance)
        {
            try
            {
                if (__instance == null) return;
                object? trader = NativeBinder.GetTsgTrader(__instance);
                string id = trader != null ? NativeBinder.GetTraderId(trader) : "";
                bool hasDlg = !string.IsNullOrEmpty(id)
                    && Plugin.RegisteredTraders.Contains(id)
                    && DialogTreeLoader.Exists(id)
                    && DialogTreeLoader.TabVisible(id);

                VisitTalkButton? marker = EnsureButton(__instance);
                if (marker == null) return;
                marker.Configure(__instance, id);
                marker.gameObject.SetActive(hasDlg);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[TalkButton] postfix: " + (ex.InnerException ?? ex).Message);
            }
        }

        // Create the button once (cloned from the trade screen's native close button), reuse it thereafter.
        private static VisitTalkButton? EnsureButton(object screen)
        {
            if (!(screen is Component screenComp)) return null;

            VisitTalkButton existing = screenComp.GetComponentInChildren<VisitTalkButton>(includeInactive: true);
            if (existing != null) return existing;

            object? closeObj = NativeBinder.TsgCloseButtonField?.GetValue(screen);
            if (!(closeObj is DefaultUIButton closeButton))
            {
                Plugin.Log.LogWarning("[TalkButton] _closeButton not found; cannot build Talk button");
                return null;
            }

            GameObject clone = UnityEngine.Object.Instantiate(closeButton.gameObject, closeButton.transform.parent, worldPositionStays: false);
            clone.name = "VisitTalkButton";

            DefaultUIButton button = clone.GetComponent<DefaultUIButton>();
            if (button != null)
            {
                button.OnClick.RemoveAllListeners();
                button.SetIcon(null, null);
            }

            // Anchor the 对话 button to the top-CENTRE of the (full-width) top bar so it sits dead-centre on screen
            // regardless of resolution, kept level with the native close button. TalkOffsetX/Y nudge from there.
            if (clone.transform is RectTransform rt)
            {
                float closeY = closeButton.transform is RectTransform src ? src.anchoredPosition.y : 0f;
                rt.anchorMin = new Vector2(0.5f, 1f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = new Vector2(Plugin.TalkOffsetX.Value, closeY + Plugin.TalkOffsetY.Value);
            }

            VisitTalkButton marker = clone.AddComponent<VisitTalkButton>();
            marker.Button = button;
            if (button != null) button.OnClick.AddListener(marker.OnTalkClicked);
            Plugin.Log.LogInfo("[TalkButton] visit button created on trade screen");
            return marker;
        }
    }
}
