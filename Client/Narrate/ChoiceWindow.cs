using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VisitAPI.ChapterUI;

namespace VisitAPI.Native;

public static class ChoiceWindow
{
    const string Name = "VisitAPI_ChoiceWindow";

    const float W = 1400f, H = 700f, HeaderH = 112.5f, BandH = 410f, QuestionH = 84.5f, ButtonsH = 91f, LineH = 1f;
    const float TitleY = 52.9f, DescY = 82.7f;
    const float BtnW = 150f, BtnH = 42.4f, BtnGap = 190f;
    const float MaskAlpha = 0.4f, FadeSeconds = 0.2f;
    const float GlowTopH = 288.0066f, GlowBottomH = 195.68f, GlowAlpha = 0.2f, HatchAlpha = 0.011764706f;

    static readonly Color Back = new(0.050980393f, 0.050980393f, 0.05490196f, 1f);
    static readonly Color Line = new(0.32941177f, 0.32941177f, 0.28235295f, 0.29803923f);
    static readonly Color TitleColor = new(0.92941177f, 0.92156863f, 0.8392157f, 1f);
    static readonly Color DescColor = new(0.7058824f, 0.7058824f, 0.6039216f, 1f);

    static readonly Dictionary<string, (string left, string right)> Images = new()
    {
        ["PopUpWarningconsequences_Sky_05_give"] = ("choice_1.png", "choice_2.png"),
        ["PopUpWarningconsequences_Sky_05_keep"] = ("choice_2.png", "choice_1.png"),
    };

    static RectTransform _live;
    static Poller _poll;

    public static bool Show(string key, Action yes, Action no)
    {
        try
        {
            var screen = DialogScreenTracker.Live;
            if (screen == null || screen.transform is not RectTransform root) { Plugin.Log.LogWarning("[choice] Dialogue screen not present, can't open the confirmation window"); return false; }
            Close();
            var s = root.rect.height > 10f ? root.rect.height / 1080f : 1f;
            var font = FontSource(root);

            var host = Box(root, Name, new Color(0f, 0f, 0f, 0f));
            Stretch(host);
            host.SetAsLastSibling();
            _live = host;
            var canvas = host.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 30000;
            host.gameObject.AddComponent<GraphicRaycaster>();
            _poll = host.gameObject.AddComponent<Poller>();
            _poll.Canvas = canvas;

            var win = Box(host, "Window", Back);
            Center(win, W * s, H * s);

            Glow(win, "GlowUP", GlowTopH * s, false);
            Glow(win, "GlowBottom", GlowBottomH * s, true);

            Text(win, "HeaderText", Word("DialogConfirmationWindow/Header", "关键抉择", "IMPORTANT CHOICE"), font, 30f * s, TitleColor, TitleY * s, 40f * s);
            Text(win, "DescriptionText", Word("DialogConfirmationWindow/Description", "关键节点，无法折返", "POINT OF NO RETURN"), font, 16f * s, DescColor, DescY * s, 26f * s);

            var band = Strip(win, "Band", Color.clear, HeaderH * s, BandH * s);
            Images.TryGetValue(key, out var art);
            var leftMask = Slot(band, "Left", art.left, 0f, 0.5f);
            var rightMask = Slot(band, "Right", art.right, 0.5f, 1f);
            Overlay(band, "Noise", "choice_noise.png", 1f);

            Strip(win, "BorderUp", Line, (HeaderH + BandH) * s, LineH * s);
            var qTop = HeaderH + BandH + LineH;
            var question = Strip(win, "Question", Color.clear, qTop * s, QuestionH * s);
            Overlay(question, "Tile", "choice_hatch.png", HatchAlpha, tiled: true, flipY: true);
            Overlay(question, "InnerShadow", "choice_qshadow.png", 1f);
            Text(win, "ChoiseText", Word(key, key, key), font, 16f * s, TitleColor, (qTop + QuestionH / 2f) * s, 30f * s);
            Strip(win, "BorderBottom", Line, (qTop + QuestionH) * s, LineH * s);

            var btnY = qTop + QuestionH + LineH + ButtonsH / 2f;
            var size = Mathf.Max(1, Mathf.RoundToInt(30f * s));
            var done = false;
            void Pick(Action act)
            {
                if (done) return;
                done = true;
                Close();
                try { act?.Invoke(); } catch (Exception e) { Plugin.Log.LogWarning("[choice] Choice callback failed: " + e.Message); }
            }
            Button(win, Word("Yes", "是", "Yes"), size, font, -BtnGap / 2f * s, btnY * s, s, () => Pick(yes), leftMask);
            Button(win, Word("No", "否", "No"), size, font, BtnGap / 2f * s, btnY * s, s, () => Pick(no), rightMask);

            Frame9(win, s);

            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[choice] Failed to build confirmation window, running this line natively: " + e);
            Close();
            return false;
        }
    }

    public static void Close()
    {
        if (_live != null) UnityEngine.Object.Destroy(_live.gameObject);
        _live = null;
        _poll = null;
    }

    static RectTransform Box(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = true;
        return rt;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    static void Center(RectTransform rt, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(w, h);
    }

    static RectTransform Strip(Transform parent, string name, Color color, float top, float height)
    {
        var rt = Box(parent, name, color);
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -top);
        rt.sizeDelta = new Vector2(0f, height);
        return rt;
    }

    static RectTransform Overlay(Transform parent, string name, string file, float alpha, bool tiled = false, bool flipY = false)
    {
        var sprite = VisitArt.Load(file);
        if (sprite == null) return null;
        var rt = Box(parent, name, new Color(1f, 1f, 1f, alpha));
        var img = rt.GetComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        if (tiled) img.type = Image.Type.Tiled;
        Stretch(rt);
        if (flipY) rt.localScale = new Vector3(1f, -1f, 1f);
        return rt;
    }

    static void Glow(RectTransform win, string name, float height, bool bottom)
    {
        var sprite = VisitArt.Load("choice_glow.png");
        if (sprite == null) return;
        var rt = Box(win, name, new Color(1f, 1f, 1f, GlowAlpha));
        var img = rt.GetComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        rt.anchorMin = new Vector2(0f, bottom ? 0f : 1f);
        rt.anchorMax = new Vector2(1f, bottom ? 0f : 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0f, height);
        if (bottom) rt.localScale = new Vector3(1f, -1f, 1f);
    }

    static void Frame9(RectTransform win, float s)
    {
        var sprite = VisitArt.Load("choice_frame.png", new Vector4(5f, 5f, 5f, 5f));
        if (sprite == null) { Plugin.Log.LogWarning("[choice] Frame texture missing, window drawn without border"); return; }
        var rt = Box(win, "Border", Color.white);
        var img = rt.GetComponent<Image>();
        img.sprite = sprite;
        img.type = Image.Type.Sliced;
        img.fillCenter = false;
        img.raycastTarget = false;
        Stretch(rt);
        rt.offsetMin = new Vector2(-5f * s, -5f * s);
        rt.offsetMax = new Vector2(5f * s, 5f * s);
    }

    static RectTransform Slot(RectTransform band, string name, string file, float x0, float x1)
    {
        var slot = Box(band, name, new Color(0f, 0f, 0f, 0f));
        slot.anchorMin = new Vector2(x0, 0f); slot.anchorMax = new Vector2(x1, 1f);
        slot.pivot = new Vector2(0.5f, 0.5f);
        slot.offsetMin = Vector2.zero; slot.offsetMax = Vector2.zero;
        var sprite = file != null ? VisitArt.Load(file) : null;
        if (sprite != null)
        {
            var img = slot.GetComponent<Image>();
            img.sprite = sprite;
            img.color = Color.white;
        }
        var mask = Box(slot, "Mask", new Color(0f, 0f, 0f, MaskAlpha));
        Stretch(mask);
        var shadow = VisitArt.Load("choice_shadow.png");
        if (shadow != null)
        {
            var inner = Box(slot, "InnerShadow", Color.white);
            inner.GetComponent<Image>().sprite = shadow;
            Stretch(inner);
        }
        return mask;
    }

    static RectTransform Text(Transform parent, string name, string text, TMP_Text font, float size, Color color, float centerY, float height)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.localScale = Vector3.one;
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, -centerY);
        rt.sizeDelta = new Vector2(-60f, height);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (font != null) { tmp.font = font.font; tmp.fontSharedMaterial = font.fontSharedMaterial; }
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        TmpFix.Set(tmp, text ?? "");
        return rt;
    }

    static void Button(RectTransform win, string text, int size, TMP_Text font, float offsetX, float centerY, float s, Action click, RectTransform mask)
    {
        RectTransform rt = null;
        try
        {
            // 09-26：以前随手取场景里第一个 DefaultUIButton 当模板，抓到过聊天窗克隆的回复按钮、跳过它之后又抓到别的带电话图标的原生按钮。
            // 现在固定用原生聊天窗的「收取全部」按钮（图标本来就是空的，聊天窗克隆的也是它），找不到才退回第一个不是我们克隆的
            var candidates = UnityEngine.Object.FindObjectsOfType<DefaultUIButton>(true)
                .Where(b => b != null && b._headerLabel != null && b.GetComponent<RectTransform>() != null
                            && b.GetComponent<VisitClonedButton>() == null && !b.name.StartsWith("Btn_", StringComparison.Ordinal)).ToList();
            var src = candidates.FirstOrDefault(b => b.name == "ReceiveAllButton") ?? candidates.FirstOrDefault();
            if (src != null)
            {
                var go = UnityEngine.Object.Instantiate(src.gameObject, win, false);
                go.name = "Btn_" + text;
                go.SetActive(true);
                // 抉择按钮只有文字：模板上的图标（不管挂在哪一层）一律关掉
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    if (t != go.transform && (t.name == "IconContainer" || t.name == "Icon" || t.name == "IconIdle")) t.gameObject.SetActive(false);
                foreach (var b in go.GetComponentsInChildren<Button>(true)) b.onClick.RemoveAllListeners();
                foreach (var le in go.GetComponentsInChildren<LayoutElement>(true)) le.ignoreLayout = true;
                if (go.GetComponent<ContentSizeFitter>() is ContentSizeFitter fitter) fitter.enabled = false;
                var btn = go.GetComponent<DefaultUIButton>();
                if (btn != null)
                {
                    btn.OnClick.RemoveAllListeners();
                    btn.SetRawText(text, size);
                    btn.Interactable = true;
                }
                rt = (RectTransform)go.transform;
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning("[choice] Failed to clone native button, using plain text button: " + e.Message); rt = null; }

        if (rt == null)
        {
            rt = Box(win, "Btn_" + text, new Color(1f, 1f, 1f, 0.04f));
            var label = Text(rt, "Text", text, font, size, TitleColor, 0f, BtnH * s);
            label.anchorMin = Vector2.zero; label.anchorMax = Vector2.one;
            label.offsetMin = Vector2.zero; label.offsetMax = Vector2.zero;
        }
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(BtnW * s, BtnH * s);
        rt.anchoredPosition = new Vector2(offsetX, win.rect.height / 2f - centerY);
        rt.localScale = Vector3.one;

        var catcher = Box(rt, "Click", new Color(0f, 0f, 0f, 0f));
        Stretch(catcher);
        catcher.SetAsLastSibling();
        var target = rt.gameObject;
        var hover = catcher.gameObject.AddComponent<Hover>();
        hover.Enter = () => { Fade(mask, 0f); Forward(target, ExecuteEvents.pointerEnterHandler); };
        hover.Exit = () => { Fade(mask, MaskAlpha); Forward(target, ExecuteEvents.pointerExitHandler); };
        hover.Down = () => Forward(target, ExecuteEvents.pointerDownHandler);
        hover.Up = () => Forward(target, ExecuteEvents.pointerUpHandler);
        hover.Click = click;
        _poll?.Add(rt, click, hover.Enter, hover.Exit);
    }

    static void Forward<T>(GameObject go, ExecuteEvents.EventFunction<T> what) where T : IEventSystemHandler
    {
        if (go == null || EventSystem.current == null) return;
        try { ExecuteEvents.Execute(go, new PointerEventData(EventSystem.current), what); }
        catch (Exception e) { Plugin.Log.LogWarning("[choice] Failed to forward button event: " + e.Message); }
    }

    static void Fade(RectTransform mask, float to)
    {
        if (mask == null) return;
        var img = mask.GetComponent<Image>();
        if (img == null) return;
        var fade = mask.GetComponent<Fader>() ?? mask.gameObject.AddComponent<Fader>();
        fade.Target = img;
        fade.To = to;
    }

    static TMP_Text FontSource(Transform root)
    {
        var t = root.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault(x => x != null && x.font != null);
        if (t == null) Plugin.Log.LogWarning("[choice] No existing text found on dialogue screen, confirmation window uses default font");
        return t;
    }

    static string Word(string key, string ch, string en)
    {
        try { var t = key.Localized(); if (!string.IsNullOrWhiteSpace(t) && t != key) return t; } catch { }
        return Loc.Pick(ch, en);
    }

    class Poller : MonoBehaviour
    {
        public Canvas Canvas;
        readonly List<(RectTransform rect, Action click, Action enter, Action exit)> _buttons = new();
        int _hover = -1;
        bool _first = true;

        public void Add(RectTransform rect, Action click, Action enter, Action exit) => _buttons.Add((rect, click, enter, exit));

        void Update()
        {
            if (_buttons.Count == 0) return;
            var pos = (Vector2)Input.mousePosition;
            var cam = Canvas != null && Canvas.renderMode != RenderMode.ScreenSpaceOverlay ? Canvas.worldCamera : null;
            var hit = -1;
            for (var i = 0; i < _buttons.Count; i++)
                if (_buttons[i].rect != null && RectTransformUtility.RectangleContainsScreenPoint(_buttons[i].rect, pos, cam)) { hit = i; break; }
            if (_first)
            {
                _first = false;
            }
            if (hit != _hover)
            {
                if (_hover >= 0) Safe(_buttons[_hover].exit);
                if (hit >= 0) Safe(_buttons[hit].enter);
                _hover = hit;
            }
            if (hit >= 0 && Input.GetMouseButtonDown(0))
            {
                Safe(_buttons[hit].click);
            }
        }

        static void Safe(Action a)
        {
            try { a?.Invoke(); } catch (Exception e) { Plugin.Log.LogWarning("[choice] Button polling callback failed: " + e.Message); }
        }
    }

    class Fader : MonoBehaviour
    {
        public Image Target;
        public float To;

        void Update()
        {
            if (Target == null) return;
            var c = Target.color;
            var a = Mathf.MoveTowards(c.a, To, Time.unscaledDeltaTime / FadeSeconds);
            if (!Mathf.Approximately(a, c.a)) Target.color = new Color(c.r, c.g, c.b, a);
        }
    }

    class Hover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        public Action Enter, Exit, Click, Down, Up;
        bool _inside;

        public void OnPointerEnter(PointerEventData e)
        {
            _inside = true;
            Safe(Enter);
        }

        public void OnPointerExit(PointerEventData e) { _inside = false; Safe(Exit); }
        public void OnPointerDown(PointerEventData e) => Safe(Down);
        public void OnPointerUp(PointerEventData e) { Safe(Up); if (_inside) Fire("pointer up"); }
        public void OnPointerClick(PointerEventData e) => Fire("click");

        void Fire(string how)
        {
            Safe(Click);
        }

        static void Safe(Action a)
        {
            try { a?.Invoke(); } catch (Exception e) { Plugin.Log.LogWarning("[choice] Button event failed: " + e.Message); }
        }
    }
}
