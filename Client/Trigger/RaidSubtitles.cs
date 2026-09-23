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
        if (!Build()) { Plugin.Log.LogWarning("[subtitle] 字幕条建不出来，这次不显示：" + why); return; }
        if (_playing != null) Plugin.Instance.StopCoroutine(_playing);
        Plugin.Log.LogInfo($"[subtitle] 播 {list.Count} 行（{why}）: {string.Join(" | ", list.Select(l => l.Key.Localized()))}");
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

    static bool Build()
    {
        if (_canvas != null && _view != null && _text != null) return true;
        SubtitlesView source = null;
        try { source = MonoBehaviourSingleton<CommonUI>.Instantiated ? MonoBehaviourSingleton<CommonUI>.Instance.TraderDialogScreen?._subtitlesView : null; }
        catch (System.Exception e) { Plugin.Log.LogWarning("[subtitle] 找对话屏字幕组件失败: " + e.Message); }
        var canvasGo = new GameObject("VisitRaidSubtitles", typeof(Canvas), typeof(CanvasScaler));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        var srcScaler = source != null ? source.GetComponentInParent<CanvasScaler>(true) : null;
        if (srcScaler != null)
        {
            scaler.uiScaleMode = srcScaler.uiScaleMode; scaler.referenceResolution = srcScaler.referenceResolution;
            scaler.screenMatchMode = srcScaler.screenMatchMode; scaler.matchWidthOrHeight = srcScaler.matchWidthOrHeight;
            scaler.referencePixelsPerUnit = srcScaler.referencePixelsPerUnit;
        }
        else { scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.matchWidthOrHeight = 0.5f; }
        _canvas = canvasGo;
        if (source != null && source._textField != null)
        {
            var clone = Object.Instantiate(source.gameObject, canvasGo.transform, false);
            clone.name = "SubtitlesView(raid)";
            {
                var crt = (RectTransform)clone.transform;
                crt.anchorMin = new Vector2(0.2925f, 0.0933f);
                crt.anchorMax = new Vector2(0.7075f, 0.1244f);
                crt.offsetMin = crt.offsetMax = Vector2.zero;
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(2560f, 1440f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 1f;
            }
            var sv = clone.GetComponent<SubtitlesView>();
            _text = sv != null ? sv._textField : clone.GetComponentInChildren<TMP_Text>(true);
            if (sv != null) Object.Destroy(sv);
            foreach (var g in clone.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
            _view = clone;
            _view.SetActive(false);
            Plugin.Log.LogInfo($"[subtitle] 战局字幕条：克隆对话屏的原生字幕组件（画布 {(srcScaler != null ? srcScaler.referenceResolution.ToString() : "默认")}）");
            return _text != null;
        }
        var font = Object.FindObjectOfType<TextMeshProUGUI>()?.font ?? Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault();
        if (font == null) return false;
        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        var prt = (RectTransform)panel.transform;
        prt.SetParent(canvasGo.transform, false);
        prt.anchorMin = prt.anchorMax = new Vector2(0f, 0f); prt.pivot = new Vector2(0f, 0f); prt.anchoredPosition = new Vector2(158f, 128f);
        var bg = panel.GetComponent<Image>(); bg.color = new Color(0f, 0f, 0f, 0.72f); bg.raycastTarget = false;
        var layout = panel.GetComponent<HorizontalLayoutGroup>(); layout.padding = new RectOffset(14, 14, 6, 6); layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        var fitter = panel.GetComponent<ContentSizeFitter>(); fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(panel.transform, false);
        var t = textGo.GetComponent<TextMeshProUGUI>();
        t.font = font; t.fontSize = 24f; t.color = Color.white; t.richText = true; t.enableWordWrapping = true; t.raycastTarget = false;
        textGo.AddComponent<LayoutElement>().preferredWidth = 1100f;
        _text = t; _view = panel; _view.SetActive(false);
        Plugin.Log.LogWarning("[subtitle] 对话屏字幕组件拿不到，退回自画字幕条");
        return true;
    }
}
