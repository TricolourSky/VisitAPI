using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VisitAPI.Native
{
    // Full-screen invisible click-catcher that advances narration subtitles one tap at a time (ported from the
    // 0.2.4 narration overlay). A top-most transparent canvas with a PointerClick handler — armed 0.4s late so
    // the very click that opened a line doesn't instantly skip it. Setting a null handler destroys the overlay.
    internal static class NarrationOverlay
    {
        private static GameObject? _overlay;

        internal static void SetClickHandler(Action? callback)
        {
            if (_overlay != null) { UnityEngine.Object.Destroy(_overlay); _overlay = null; }
            if (callback == null) return;

            _overlay = new GameObject("VisitAPI.NarrationOverlay");
            UnityEngine.Object.DontDestroyOnLoad(_overlay);
            Canvas canvas = _overlay.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 3500;
            _overlay.AddComponent<GraphicRaycaster>();

            GameObject clickGo = new GameObject("Click");
            clickGo.transform.SetParent(_overlay.transform, false);
            Image img = clickGo.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f);
            RectTransform rt = img.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            EventTrigger trigger = clickGo.AddComponent<EventTrigger>();
            EventTrigger.Entry entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            float armAt = Time.unscaledTime + 0.4f;
            entry.callback.AddListener(_ => { if (Time.unscaledTime >= armAt) callback(); });
            trigger.triggers.Add(entry);
        }
    }
}
