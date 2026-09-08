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

/// <summary>
/// 1.1 的商人加载屏（PreloaderUIScene 里的 DialogueLoadingScreen：故障风动态背景 + 转圈计时环 + 商人名牌，SORA 2026-09-05 要求搬过来）。
/// 0.16 引擎在访问流程里弹的是藏身处那张加载屏（`NarrateController.Show` 开头 `HideoutLoadingScreen.Show()`、结尾 `Close()`），
/// 这里在同一对入口上换成 1.1 的：`Visit()` 先 <see cref="Arm"/>，引擎一调 Show 就改弹我们的，Close 时一起收。
/// 1.1 的 `NarrateLoadingScreen` 类 0.16 没有，抽取时已剥掉；它的 Show(名字)/Close 由这里做：名字进 TMP、淡入淡出走 CanvasGroup，
/// 背景故障动画和计时环由预制体自带的两个 Animator 循环播放。字体：包里 TMP 的字体引用是空的（1.1 字体资产没带），
/// 从 PreloaderUI 现成的文字上抄（章节屏同款做法，DEV_NOTES #69）。
/// 包：plugins/VisitAPI/bundles/visitapi_narrateloading.bundle（IsolatedSDK Assets/VisitAPI/NarrateLoading，VendorScene uiprefab 抽的）。
/// </summary>
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

    /// <summary>Visit() 在调引擎 Show 之前武装一次：接下来那一次 HideoutLoadingScreen.Show 改弹我们的。
    /// 10 秒内有效——流程半路断掉的话，别把标记串到下一次进藏身处的加载上。</summary>
    public static void Arm(string traderId)
    {
        var key = traderId + " Nickname";
        var nick = key.Localized();
        _pending = string.IsNullOrEmpty(nick) || nick == key ? traderId : nick;
        _pendingUntil = Time.unscaledTime + 10f;
    }

    /// 上一次 HideoutLoadingScreen.Show 是不是被我们拦下改弹了自己的。Close 只对这种配对调用才接管（09-07 终审）：
    /// 以前只看 Shown——任何原因让 Shown 残留（访问在 Show 阶段失败没走到 Close），玩家之后正常进藏身处，
    /// 藏身处加载屏真的弹了、Close 却被我们吞掉，那张屏永远挂着。
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
        foreach (var animator in animators) { animator.Rebind(); animator.Update(0f); }   // 从头播，别接着上次关掉时的那一帧
        Shown = true;
        _intercepted = true;
        _shownAt = Time.unscaledTime;
        if (_hold != null) { Plugin.Instance.StopCoroutine(_hold); _hold = null; }
        StartFade(1f, null);
        Plugin.Log.LogInfo($"[narrate] 1.1 加载屏: {name}（Animator {animators.Length} 个）");
        return false;
    }

    /// 访问在引擎调 Close 之前就中止时（NarrateEntry.Abort 的本地兜底）：立刻把我们的屏收掉，标记全清
    internal static void ForceClose()
    {
        _pending = null;
        _intercepted = false;
        if (!Shown) return;
        Shown = false;
        if (_hold != null) { Plugin.Instance.StopCoroutine(_hold); _hold = null; }
        Hide();
    }

    /// <summary>最短显示时长（SORA 09-07 定：大概 2 秒）。资源包常驻后房间 1 秒左右就加载完，加载屏一闪而过，
    /// 计时环（一圈 5 秒多）和故障背景根本来不及被看见——探针日志证明动画本身一直在走（09-07）。引擎调 Close 时不足 2 秒就先等够再淡出。</summary>
    const float MinShow = 2f;
    static float _shownAt;
    static Coroutine _hold;

    internal static bool CloseInstead()
    {
        if (!_intercepted) return true;   // 上一次 Show 没被我们拦下（藏身处自己的加载屏）：原方法照跑
        _intercepted = false;
        if (!Shown) return true;
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
        if (!Shown) Hide();   // 等待期间又 Show 了一次（连续访问）就别关
    }

    static void Hide() => StartFade(0f, () => { if (_screen != null) _screen.SetActive(false); });

    static bool Ensure(HideoutLoadingScreen hideout)
    {
        if (_screen != null) return true;
        var prefab = Load()?.LoadAsset<GameObject>(PrefabName);
        if (prefab == null) { Plugin.Log.LogWarning("[narrate] 加载屏 prefab 不在包里: " + PrefabName); return false; }
        var parent = hideout != null ? hideout.transform.parent : null;
        if (parent == null) { Plugin.Log.LogWarning("[narrate] 加载屏没有可挂的父节点"); return false; }
        // 09-06 实机：挂在藏身处加载屏的父节点下时，背景 20 帧和计时环 7 段（都按父节点拉伸）被压成零尺寸，只剩固定尺寸的光斑和名牌——
        // 那个父节点是个 0×0 的容器。改挂到根画布上，尺寸跟屏幕走。
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
        // 加载期间引擎把 Time.timeScale 压成 0（藏身处加载屏自己的转圈用的是 DOTween 的独立时间），预制体里的两个 Animator
        // 是按缩放时间走的，会冻在第一帧——故障背景不轮播、计时环不呼吸，看到的就是一块静止的白（09-06 SORA 实机）。改成不受时间刻度影响。
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

    /// PreloaderUI 整棵树上任意一条带字体的 TMP（FPS 计数 / 版本号 / 通知栏……）
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
        var path = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "bundles", BundleFile);
        if (!File.Exists(path)) { Plugin.Log.LogWarning("[narrate] 加载屏包不存在: " + path); return null; }
        _bundle = AssetBundle.LoadFromFile(path);
        if (_bundle == null) Plugin.Log.LogWarning("[narrate] 加载屏包加载失败: " + path);
        return _bundle;
    }
}

/// <summary>引擎在访问流程开头弹藏身处加载屏——武装过就改弹 1.1 的商人加载屏。</summary>
[HarmonyPatch(typeof(HideoutLoadingScreen), nameof(HideoutLoadingScreen.Show))]
public static class NarrateLoadingShow
{
    static bool Prefix(HideoutLoadingScreen __instance) => NarrateLoading.ShowInstead(__instance);
}

/// <summary>访问流程结尾引擎关藏身处加载屏——我们的在显示就关我们的（藏身处那张压根没开过，别去关它）。</summary>
[HarmonyPatch(typeof(HideoutLoadingScreen), nameof(HideoutLoadingScreen.Close))]
public static class NarrateLoadingClose
{
    static bool Prefix() => NarrateLoading.CloseInstead();
}
