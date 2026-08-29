using Comfort.Common;
using EFT.Communications;
using EFT.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VisitAPI.ChapterUI
{
    /// <summary>1.1 的 MainQuestNotificationView 骨架：字段名照 dump（bundle 里的 prefab 按名连线），基类是 0.16 真的 BaseNotificationView——
    /// 立起/躺下动画、音效、排队、点击关闭全借它；我们只管换底图（章节/子任务）、对勾（开始/完成/失败）、标题。DEV_NOTES #72。</summary>
    public class MainQuestNotificationView : BaseNotificationView
    {
        [SerializeField] public TextMeshProUGUI _title;   // prefab 里是 CustomTextMeshProUGUI（它的子类），按基类接住就不用碰那个已标废弃的类型
        [SerializeField] public Image _checkmarkIcon;
        [SerializeField] public Sprite _chapterBackgroundSprite, _subtaskBackgroundSprite, _checkmarkStartedSprite, _checkmarkSuccessSprite, _checkmarkFailSprite;
        public override bool ReturnToPool => false;   // 不是默认横幅那种池化件，躺下后直接销毁

        public void Init(ChapterBanner n)
        {
            if (_background != null) _background.sprite = n.IsChapter ? _chapterBackgroundSprite : _subtaskBackgroundSprite;
            if (_checkmarkIcon != null) _checkmarkIcon.sprite = n.Status == ChapterBanner.EStatus.Success ? _checkmarkSuccessSprite : n.Status == ChapterBanner.EStatus.Fail ? _checkmarkFailSprite : _checkmarkStartedSprite;
            if (_title != null) TmpFix.Set(_title, n.Title ?? "");
            Init((Notification)n);   // 底盘：图标/正文/底色/动画速度/立起
            if (_text != null) TmpFix.Set(_text, _text.text);
            if (n.Sprite != null && _icon != null) _icon.sprite = n.Sprite;
        }
    }

    /// <summary>章节横幅通知（1.1 的 NotificationMainQuest）：Title = 章节/子任务名，Text = 状态行，IsChapter 选底图，Status 选对勾，Sprite = 章节图标。
    /// 视图从 bundle 实例化到通知栏容器里；bundle 缺失或 prefab 连线不全时退回 VisitBanner 那种默认横幅。</summary>
    public class ChapterBanner : NotificationWithText
    {
        public enum EStatus { Started, Success, Fail }
        public string Title; public Sprite Sprite; public bool IsChapter; public EStatus Status;
        public AudioClip Clip;   // 1.1 的剧情音效（bundle 里的 story_* 片段，DEV_NOTES #73）；null 就照常按 SoundType 放
        public bool Silent;      // 1.1 在这个时刻根本不出通知（子任务开始/完成）：横幅照出，一声不吭
        public override ENotificationIconType Icon => ENotificationIconType.Quest;
        public override Color? BackgroundColor => Color.white;   // 底图是 1.1 的 sprite，不让默认那层半透明黑压它
        public override bool ShowImmediately => true;

        public override BaseNotificationView CreateView(INotificationViewFactory viewFactory)
        {
            var notifier = viewFactory as NotifierView;
            var font = notifier != null && notifier._defaultNotificationTemplate != null ? notifier._defaultNotificationTemplate._text as TextMeshProUGUI : null;
            var go = notifier != null ? ChapterBundle.Instantiate("MainQuestNotification", notifier._container, font) : null;
            var view = go != null ? go.GetComponent<MainQuestNotificationView>() : null;
            if (view == null || view._icon == null || view._text == null)
            {
                if (go != null) Object.Destroy(go);
                Plugin.Log.LogWarning("[chapter/banner] 1.1 notification view unavailable, using default banner");
                return viewFactory.CreateDefaultView(this);
            }
            if (view._container == null) view._container = notifier._container;   // 1.1 场景里这个字段指的是通知栏容器本身（在 prefab 之外），运行时补回
            notifier.SetupNotificationView(view);
            view.Init(this);
            return view;
        }

        /// NotifierView 出横幅时按 SoundType 放 UI 音效；带自定义片段的横幅改放 1.1 那段，走 GUISounds 同一个 UI 音源（音量/混音组一致）
        [HarmonyPatch(typeof(NotifierView), nameof(NotifierView.PlaySound))]
        static class SoundPatch
        {
            static bool Prefix(Notification notification)
            {
                if (!(notification is ChapterBanner b)) return true;
                if (b.Silent) return false;
                if (b.Clip == null) return true;
                if (Singleton<GUISounds>.Instantiated) Singleton<GUISounds>.Instance.PlaySound(b.Clip);
                return false;
            }
        }
    }
}
