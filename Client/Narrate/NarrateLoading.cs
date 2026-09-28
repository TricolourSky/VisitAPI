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
            if (Shown) { Plugin.Log.LogWarning("[narrate] 1.1 loading screen left over (last visit never reached Close), closing it before letting the hideout loading screen through"); ForceClose(); }
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
        if (prefab == null) { Plugin.Log.LogWarning("[narrate] Loading screen prefab not found in bundle: " + PrefabName); return false; }
        var parent = hideout != null ? hideout.transform.parent : null;
        if (parent == null) { Plugin.Log.LogWarning("[narrate] Loading screen has no parent node to attach to"); return false; }
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
        else Plugin.Log.LogWarning("[narrate] Loading screen found no font to copy, trader name may not display");
        _screen.SetActive(false);
        return true;
    }

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
        if (!File.Exists(path)) { Plugin.Log.LogWarning("[narrate] Loading screen bundle does not exist: " + path); return null; }
        _bundle = AssetBundle.LoadFromFile(path);
        if (_bundle == null) Plugin.Log.LogWarning("[narrate] Failed to load loading screen bundle: " + path);
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
