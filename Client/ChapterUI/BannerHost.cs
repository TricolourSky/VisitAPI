using System;
using System.Collections.Generic;
using EFT.Communications;
using EFT.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using VisitAPI.Native;

namespace VisitAPI.ChapterUI;

public static class BannerHost
{
    const string Name = "VisitAPI_BannerHost";
    static string _loggedFor;
    static readonly List<(BaseNotificationView view, RectTransform back, float flex)> _hosted = new();

    public static bool Attach(BaseNotificationView view, NotifierView notifier)
    {
        var dialog = DialogScreenTracker.Live;
        if (dialog == null || view == null || notifier == null || notifier._container == null) return false;
        try
        {
            if (dialog.transform is not RectTransform root) return false;
            var le = view.GetComponent<LayoutElement>();
            var bannerH = le != null && le.preferredHeight > 0f ? le.preferredHeight : le != null && le.minHeight > 0f ? le.minHeight : 110f;
            var host = Ensure(root, notifier._container, bannerH);
            view.transform.SetParent(host, false);
            view.transform.SetAsLastSibling();
            var flex = le != null ? le.flexibleHeight : -1f;
            if (le != null) le.flexibleHeight = 0f;
            var back = notifier._container;
            _hosted.Add((view, back, flex));
            void Restore(Notification _, BaseNotificationView v)
            {
                v.OnHideComplete -= Restore;
                _hosted.RemoveAll(h => h.view == v);
                if (v != null && le != null) le.flexibleHeight = flex;
                if (v != null && back != null && v.transform.parent == host) v.transform.SetParent(back, false);
            }
            view.OnHideComplete += Restore;
            return true;
        }
        catch (Exception e) { Plugin.Log.LogWarning("[banner] 挪到对话屏失败，按原位显示: " + e.Message); return false; }
    }

    public static void ReturnAll()
    {
        if (_hosted.Count == 0) return;
        var n = 0;
        foreach (var (view, back, flex) in _hosted)
        {
            try
            {
                if (view == null || back == null) continue;
                var parent = view.transform.parent;
                if (parent == null || parent.name != Name) continue;
                if (view.GetComponent<LayoutElement>() is LayoutElement l) l.flexibleHeight = flex;
                view.transform.SetParent(back, false);
                view.transform.SetAsLastSibling();
                n++;
            }
            catch (Exception e) { Plugin.Log.LogWarning("[banner] 横幅送回通知栏失败: " + e.Message); }
        }
        _hosted.Clear();
        if (n > 0) Plugin.Log.LogInfo($"[banner] 对话屏关闭，{n} 条未播完的横幅送回通知栏");
    }

    [HarmonyPatch(typeof(TraderDialogScreen), "Close")]
    public static class CloseGuard
    {
        static void Prefix() => ReturnAll();
    }

    static RectTransform Ensure(RectTransform dialogRoot, RectTransform container, float bannerH)
    {
        var host = dialogRoot.Find(Name) as RectTransform;
        if (host == null)
        {
            host = new GameObject(Name, typeof(RectTransform)).GetComponent<RectTransform>();
            host.SetParent(dialogRoot, false);
            var lg = container.GetComponent<LayoutGroup>();
            if (lg != null && host.gameObject.AddComponent(lg.GetType()) is LayoutGroup copy)
                for (var t = lg.GetType(); t != null && t != typeof(Component); t = t.BaseType) Reflect.Copy(lg, copy, t, out _);
        }
        if (host.GetComponent<LayoutGroup>() is HorizontalOrVerticalLayoutGroup hv) { hv.childControlHeight = true; hv.childForceExpandHeight = false; }
        host.SetAsLastSibling();
        var corners = new Vector3[4];
        container.GetWorldCorners(corners);
        var bl = dialogRoot.InverseTransformPoint(corners[0]);
        var tr = dialogRoot.InverseTransformPoint(corners[2]);
        var size = new Vector2(tr.x - bl.x, tr.y - bl.y);
        host.anchorMin = host.anchorMax = Vector2.zero;
        host.pivot = Vector2.zero;
        if (size.x > 10f && size.y > 10f && size.x < 8000f && size.y < 8000f)
        {
            var align = host.GetComponent<LayoutGroup>()?.childAlignment ?? TextAnchor.UpperLeft;
            var lower = align == TextAnchor.LowerLeft || align == TextAnchor.LowerCenter || align == TextAnchor.LowerRight;
            var height = Mathf.Max(size.y, bannerH * 3f);
            host.sizeDelta = new Vector2(size.x, height);
            host.anchoredPosition = lower
                ? new Vector2(bl.x - dialogRoot.rect.xMin, bl.y - dialogRoot.rect.yMin)
                : new Vector2(bl.x - dialogRoot.rect.xMin, tr.y - dialogRoot.rect.yMin - height);
        }
        else
        {
            host.anchorMin = host.anchorMax = new Vector2(0.5f, 1f);
            host.pivot = new Vector2(0.5f, 1f);
            host.anchoredPosition = Vector2.zero;
            host.sizeDelta = new Vector2(Mathf.Max(container.rect.width, 600f), Mathf.Max(container.rect.height, 120f));
        }
        var key = dialogRoot.GetInstanceID() + ":" + host.anchoredPosition + host.sizeDelta;
        if (_loggedFor != key)
        {
            _loggedFor = key;
            var nc = container.GetComponentInParent<Canvas>(); var dc = dialogRoot.GetComponentInParent<Canvas>();
            Plugin.Log.LogInfo($"[banner] 对话屏宿主就位：通知栏画布 {Describe(nc)}，对话屏画布 {Describe(dc)}，容器屏幕矩形 ({bl.x:0},{bl.y:0})-({tr.x:0},{tr.y:0}) → 宿主 pos={host.anchoredPosition} size={host.sizeDelta} 布局={(container.GetComponent<LayoutGroup>()?.GetType().Name ?? "无")} 横幅首选高 {bannerH:0}");
        }
        return host;
    }

    static string Describe(Canvas c) =>
        c == null ? "无" : $"{c.rootCanvas.name}/{c.renderMode}/order {c.rootCanvas.sortingOrder}/scale {c.rootCanvas.scaleFactor:0.###}";
}
