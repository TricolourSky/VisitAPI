using System;
using System.Collections;
using EFT.Hideout;
using HarmonyLib;
using TMPro;
using UnityEngine;
using VisitAPI.ChapterUI;
using Object = UnityEngine.Object;

namespace VisitAPI.Native;

public static class NarrateLoading
{
    const string PrefabName = "NarrateLoadingScreen";
    const float Fade = 0.25f;

    // 读包 / 实例化 / 删子网格 / 抄字体走 UiBundle（和章节界面包同一套）
    static readonly UiBundle Bundle = new("visitapi_narrateloading.bundle",
        "[narrate] Loading screen bundle does not exist: {0}", "[narrate] Failed to load loading screen bundle: {0}", "[narrate] Loading screen prefab not found in bundle: {0}");
    static GameObject _screen;
    static CanvasGroup _group;
    static TMP_Text _name;
    static string _pending;
    static float _pendingUntil;
    static Coroutine _fade;

    public static bool Shown { get; private set; }

    public static void Arm(string traderId)
    {
        _pending = Loc.TraderName(traderId);
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
        foreach (var animator in _screen.GetComponentsInChildren<Animator>(true)) { animator.Rebind(); animator.Update(0f); }
        Shown = true;
        _intercepted = true;
        _shownAt = Time.unscaledTime;
        StopHold();
        StartFade(1f, null);
        return false;
    }

    internal static void ForceClose()
    {
        _pending = null;
        _intercepted = false;
        if (!Shown) return;
        Shown = false;
        StopHold();
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
        StopHold();
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

    static void StopHold()
    {
        if (_hold == null) return;
        Plugin.Instance.StopCoroutine(_hold);
        _hold = null;
    }

    static void Hide() => StartFade(0f, () => { if (_screen != null) _screen.SetActive(false); });

    static bool Ensure(HideoutLoadingScreen hideout)
    {
        if (_screen != null) return true;
        var prefab = Bundle.Prefab(PrefabName);
        if (prefab == null) return false;
        var parent = hideout != null ? hideout.transform.parent : null;
        if (parent == null) { Plugin.Log.LogWarning("[narrate] Loading screen has no parent node to attach to"); return false; }
        var canvas = parent.GetComponentInParent<Canvas>();
        var host = canvas != null ? canvas.rootCanvas.transform : parent;
        _screen = Object.Instantiate(prefab, host, false);
        _screen.name = "VisitAPI_NarrateLoadingScreen";
        if (_screen.transform is RectTransform rt) rt.Stretch().localScale = Vector3.one;
        _group = _screen.GetComponent<CanvasGroup>() ?? _screen.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        foreach (var animator in _screen.GetComponentsInChildren<Animator>(true))
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        // 字体模板从加载屏所在的画布根上找现成的文字（别把我们自己这份算进去）；名字那一格显示时才 TmpFix.Set，这里不用重排（refit: false）
        var template = UiBundle.FirstFont(parent.root, _screen.transform);
        UiBundle.CopyFont(_screen, template, refit: false);
        if (template == null) Plugin.Log.LogWarning("[narrate] Loading screen found no font to copy, trader name may not display");
        _name = _screen.GetComponentInChildren<TMP_Text>(true);
        _screen.SetActive(false);
        return true;
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
