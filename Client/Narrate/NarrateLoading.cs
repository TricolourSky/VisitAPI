using System;
using System.Collections;
using System.IO;
using EFT;
using EFT.Hideout;
using HarmonyLib;
using TMPro;
using UnityEngine;
using VisitAPI.ChapterUI;
using Object = UnityEngine.Object;

namespace VisitAPI.Native;

public static class NarrateLoading
{
    const string BundleFile = "visitapi_narrateloading.bundle";
    const string PrefabName = "NarrateLoadingScreen";
    const float Fade = 0.25f;

    static AssetBundle _bundle;
    static GameObject _screen;
    static CanvasGroup _group;
    static TMP_Text _name;
    static string _pending;
    static float _pendingUntil;
    static Coroutine _fade;

    public static bool Shown { get; private set; }

    public static void Arm(string traderId)
    {
        var key = traderId + " Nickname";
        var nick = key.Localized();
        _pending = string.IsNullOrEmpty(nick) || nick == key ? traderId : nick;
        _pendingUntil = Time.unscaledTime + 10f;
    }

    static bool _intercepted;

    internal static bool ShowInstead(HideoutLoadingScreen hideout)
    {
        if (_pending == null || Time.unscaledTime > _pendingUntil)
        {
            _pending = null;
            if (Shown) { Plugin.Log.LogWarning("[narrate] 1.1 加载屏残留着（上次访问没走到 Close），先收掉再放行藏身处加载屏"); ForceClose(); }
            _intercepted = false;
            return true;
        }
        var name = _pending;
        _pending = null;
        if (!Ensure(hideout)) { _intercepted = false; return true; }
        if (_name != null) TmpFix.Set(_name, name);
        _screen.transform.SetAsLastSibling();
        _screen.SetActive(true);
        var animators = _screen.GetComponentsInChildren<Animator>(true);
        foreach (var animator in animators) { animator.Rebind(); animator.Update(0f); }
        Shown = true;
        _intercepted = true;
        _shownAt = Time.unscaledTime;
        if (_hold != null) { Plugin.Instance.StopCoroutine(_hold); _hold = null; }
        StartFade(1f, null);
        Plugin.Log.LogInfo($"[narrate] 1.1 加载屏: {name}（Animator {animators.Length} 个）");
        return false;
    }

    internal static void ForceClose()
    {
        _pending = null;
        _intercepted = false;
        if (!Shown) return;
        Shown = false;
        if (_hold != null) { Plugin.Instance.StopCoroutine(_hold); _hold = null; }
        Hide();
    }

    const float MinShow = 2f;
    static float _shownAt;
    static Coroutine _hold;

    internal static bool CloseInstead()
    {
        if (!_intercepted) return true;
        _intercepted = false;
        Shown = false;
        if (_hold != null) Plugin.Instance.StopCoroutine(_hold);
        var remain = MinShow - (Time.unscaledTime - _shownAt);
        if (remain > 0f) _hold = Plugin.Instance.StartCoroutine(HoldThenHide(remain));
        else Hide();
        return false;
    }

    static IEnumerator HoldThenHide(float seconds)
    {
        var until = Time.unscaledTime + seconds;
        while (Time.unscaledTime < until) yield return null;
        _hold = null;
        if (!Shown) Hide();
    }

    static void Hide() => StartFade(0f, () => { if (_screen != null) _screen.SetActive(false); });

    static bool Ensure(HideoutLoadingScreen hideout)
    {
        if (_screen != null) return true;
        var prefab = Load()?.LoadAsset<GameObject>(PrefabName);
        if (prefab == null) { Plugin.Log.LogWarning("[narrate] 加载屏 prefab 不在包里: " + PrefabName); return false; }
        var parent = hideout != null ? hideout.transform.parent : null;
        if (parent == null) { Plugin.Log.LogWarning("[narrate] 加载屏没有可挂的父节点"); return false; }
        var canvas = parent.GetComponentInParent<Canvas>();
        var host = canvas != null ? canvas.rootCanvas.transform : parent;
        _screen = Object.Instantiate(prefab, host, false);
        _screen.name = "VisitAPI_NarrateLoadingScreen";
        if (_screen.transform is RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }
        Plugin.Log.LogInfo($"[narrate] 加载屏挂载: host='{host.name}' {Size(host)} 原父节点='{parent.name}' {Size(parent)} 本体 {Size(_screen.transform)} "
            + $"Background {Size(_screen.transform.Find("Background"))} Loader {Size(_screen.transform.Find("Loader"))}");
        _group = _screen.GetComponent<CanvasGroup>() ?? _screen.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        foreach (var animator in _screen.GetComponentsInChildren<Animator>(true))
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        foreach (var sub in _screen.GetComponentsInChildren<TMP_SubMeshUI>(true)) Object.DestroyImmediate(sub.gameObject);
        _name = _screen.GetComponentInChildren<TMP_Text>(true);
        var template = FontTemplate(parent);
        if (template != null)
            foreach (var t in _screen.GetComponentsInChildren<TMP_Text>(true))
            {
                t.font = template.font;
                t.fontSharedMaterial = template.fontSharedMaterial;
            }
        else Plugin.Log.LogWarning("[narrate] 加载屏没找到可抄的字体，商人名可能不显示");
        _screen.SetActive(false);
        Plugin.Log.LogInfo($"[narrate] 1.1 加载屏已实例化：名牌文字={(_name != null)} 字体模板={(template != null ? template.font?.name : "无")}");
        return true;
    }

    static string Size(Transform t) => t is RectTransform r ? $"{r.rect.width:0}x{r.rect.height:0}" : (t == null ? "无" : "非Rect");

    static TMP_Text FontTemplate(Transform parent)
    {
        foreach (var t in parent.root.GetComponentsInChildren<TMP_Text>(true))
            if (t.font != null && (_screen == null || !t.transform.IsChildOf(_screen.transform))) return t;
        return null;
    }

    static void StartFade(float to, Action done)
    {
        if (_fade != null) Plugin.Instance.StopCoroutine(_fade);
        _fade = Plugin.Instance.StartCoroutine(FadeTo(to, done));
    }

    static IEnumerator FadeTo(float to, Action done)
    {
        if (_group == null) { done?.Invoke(); yield break; }
        var from = _group.alpha;
        for (var t = 0f; t < Fade; t += Time.unscaledDeltaTime)
        {
            _group.alpha = Mathf.Lerp(from, to, t / Fade);
            yield return null;
        }
        _group.alpha = to;
        _fade = null;
        done?.Invoke();
    }

    static AssetBundle Load()
    {
        if (_bundle != null) return _bundle;
        var path = Path.Combine(VisitPaths.Ui, BundleFile);   // ui\（老的 bundles\ 还认，见 VisitPaths）
        if (!File.Exists(path)) { Plugin.Log.LogWarning("[narrate] 加载屏包不存在: " + path); return null; }
        _bundle = AssetBundle.LoadFromFile(path);
        if (_bundle == null) Plugin.Log.LogWarning("[narrate] 加载屏包加载失败: " + path);
        return _bundle;
    }
}

[HarmonyPatch(typeof(HideoutLoadingScreen), nameof(HideoutLoadingScreen.Show))]
public static class NarrateLoadingShow
{
    static bool Prefix(HideoutLoadingScreen __instance) => NarrateLoading.ShowInstead(__instance);
}

[HarmonyPatch(typeof(HideoutLoadingScreen), nameof(HideoutLoadingScreen.Close))]
public static class NarrateLoadingClose
{
    static bool Prefix() => NarrateLoading.CloseInstead();
}
