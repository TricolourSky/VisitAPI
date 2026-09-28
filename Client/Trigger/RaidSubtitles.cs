using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VisitAPI.Native;

public static class RaidSubtitles
{
    public class Line { public string Key; public float Start, End = 5f; }

    static GameObject _canvas, _view;
    static TMP_Text _text;
    static Coroutine _playing;

    public static void Play(IEnumerable<Line> lines, string why)
    {
        var list = lines?.Where(l => l != null && !string.IsNullOrEmpty(l.Key)).OrderBy(l => l.Start).ToList();
        if (list == null || list.Count == 0) return;
        if (!Build()) { Plugin.Log.LogWarning("[subtitle] could not build the subtitle bar; not showing this time: " + why); return; }
        SyncScale();
        if (_playing != null) Plugin.Instance.StopCoroutine(_playing);
        _playing = Plugin.Instance.StartCoroutine(Run(list));
    }

    static IEnumerator Run(List<Line> lines)
    {
        var t0 = Time.unscaledTime;
        var end = lines.Max(l => l.End);
        while (Time.unscaledTime - t0 < end && _text != null)
        {
            var now = Time.unscaledTime - t0;
            var active = lines.Where(l => now >= l.Start && now < l.End).Select(l => l.Key.Localized()).Where(s => !string.IsNullOrEmpty(s)).ToList();
            var text = string.Join("\n", active);
            if (_text.text != text) _text.text = text;
            if (_view != null && _view.activeSelf != (text.Length > 0)) _view.SetActive(text.Length > 0);
            yield return null;
        }
        if (_view != null) _view.SetActive(false);
        if (_text != null) _text.text = string.Empty;
        _playing = null;
    }

    /// 09-26 照 1.1 重做（1.1MCP 导出 1.1 的 Preloader UI/UIContext/SubtitlesController/SubtitlesView）：1.1 的战局字幕挂在 Preloader UI 根画布上
    ///（固定像素、缩放 = 屏高 / 1080），条宽 800、底边居中离底 100、按文字高度伸缩；底 = 黑 0.698、边框 = border_generic 九宫格 (0.322,0.349,0.353)；
    /// 文字 Bender Normal 18、(0.843,0.851,0.851)、左上对齐、边距 18/8/18/12。0.16 没有 SubtitlesController（1.1 才加），这里照参数建一条。
    /// 以前是克隆对话屏字幕挂到自建画布，缩放取错（09-25 / 09-26 两次「字幕太小」）
    const float Width = 800f, Bottom = 100f, FontSize = 18f;

    static void SyncScale()
    {
        if (_canvas == null) return;
        var scaler = _canvas.GetComponent<CanvasScaler>();
        if (scaler == null) return;
        var preloader = MonoBehaviourSingleton<PreloaderUI>.Instantiated ? MonoBehaviourSingleton<PreloaderUI>.Instance.GetComponentInParent<Canvas>(true) : null;
        var f = preloader != null ? preloader.rootCanvas.scaleFactor : 0f;
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = f > 0.01f ? f : Screen.height / 1080f;
    }

    static bool Build()
    {
        if (_canvas != null && _view != null && _text != null) return true;
        var font = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f => f != null && f.name == "Jovanny Lemonad - Bender Normal SDF")
                   ?? Object.FindObjectOfType<TextMeshProUGUI>()?.font ?? Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault();
        if (font == null) { Plugin.WarnOnce("subtitle/font", "[subtitle] No TMP font found; raid subtitles cannot be shown"); return false; }
        var canvasGo = new GameObject("VisitRaidSubtitles", typeof(Canvas), typeof(CanvasScaler));
        Object.DontDestroyOnLoad(canvasGo);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;
        _canvas = canvasGo;

        var view = new GameObject("SubtitlesView(raid)", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        var vrt = (RectTransform)view.transform;
        vrt.SetParent(canvasGo.transform, false);
        vrt.anchorMin = vrt.anchorMax = new Vector2(0.5f, 0f);
        vrt.pivot = new Vector2(0.5f, 0f);
        vrt.anchoredPosition = new Vector2(0f, Bottom);
        vrt.sizeDelta = new Vector2(Width, 40f);
        var vlg = view.GetComponent<VerticalLayoutGroup>();
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        view.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Backdrop(vrt, "Background", null, new Color(0f, 0f, 0f, 0.698f));
        var border = Resources.FindObjectsOfTypeAll<Sprite>().FirstOrDefault(s => s != null && s.name == "border_generic");
        if (border != null) Backdrop(vrt, "Border", border, new Color(0.322f, 0.349f, 0.353f, 1f));
        else Plugin.Log.LogWarning("[subtitle] Sprite border_generic not found; the raid subtitle bar has no border");

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(vrt, false);
        var t = textGo.GetComponent<TextMeshProUGUI>();
        t.font = font; t.fontSize = FontSize; t.enableAutoSizing = false;
        t.color = new Color(0.843f, 0.851f, 0.851f, 1f);
        t.alignment = TextAlignmentOptions.TopLeft;
        t.margin = new Vector4(18f, 8f, 18f, 12f);
        t.richText = true; t.enableWordWrapping = true; t.raycastTarget = false;

        _text = t; _view = view; _view.SetActive(false);
        SyncScale();
        return true;
    }

    static void Backdrop(RectTransform parent, string name, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        go.GetComponent<LayoutElement>().ignoreLayout = true;
        var img = go.GetComponent<Image>();
        img.sprite = sprite; img.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        img.color = color; img.raycastTarget = false;
    }
}
