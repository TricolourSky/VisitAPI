using EFT.UI;
using HarmonyLib;

namespace VisitAPI.ChapterUI
{
    /// <summary>09-25 SORA 实机日志：章节界面每开一次，行视图里的三个 DefaultUIButton（去找商人 / 电台 / 现场访问）就抛 4 个空引用——
    /// 章节界面包是在隔离 SDK 里用桩类打的，桩类里没有 [HideInInspector] 的 _button，TweenAnimatedButton 的 _animation 是 Odin 序列化的接口、桩里也带不过来，
    /// 于是 Awake 里 _button.OnClick 空引用（点击事件没接上），OnEnable / OnDisable / 悬停按下里 _animation 空引用。
    /// 这里在引擎用到之前补齐：_button 取同物体上的 TweenAnimatedButton（原生有 RequireComponent），_animation 为空就给一个什么都不做的动画。
    /// 原生按钮这两样都不会是空的，所以只影响我们包里的按钮。</summary>
    [HarmonyPatch(typeof(DefaultUIButton), nameof(DefaultUIButton.Awake))]
    public static class ButtonStubGuard
    {
        static void Prefix(DefaultUIButton __instance)
        {
            if (__instance == null || __instance._button != null) return;
            var tween = __instance.GetComponent<TweenAnimatedButton>();
            if (tween == null) tween = __instance.gameObject.AddComponent<TweenAnimatedButton>();
            Fill(tween);
            __instance._button = tween;
        }

        internal static void Fill(TweenAnimatedButton tween)
        {
            if (tween != null && tween._animation == null) tween._animation = new NoAnimation();
        }

        sealed class NoAnimation : IButtonAnimation
        {
            public void TransitionToState(EButtonAnimationState state) { }
            public void SetState(EButtonAnimationState state) { }
            public void Stop() { }
        }
    }

    /// 没挂 DefaultUIButton 的桩 TweenAnimatedButton 也兜住：第一次启用前补上空动画
    [HarmonyPatch(typeof(TweenAnimatedButton), nameof(TweenAnimatedButton.OnEnable))]
    public static class TweenStubGuard
    {
        static void Prefix(TweenAnimatedButton __instance) => ButtonStubGuard.Fill(__instance);
    }
}
