using Comfort.Common;
using EFT.Communications;
using EFT.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace VisitAPI.ChapterUI
{
    public class ChapterBanner : NotificationWithText
    {
        public enum EStatus { Started, Success, Fail }
        public string Title; public Sprite Sprite; public bool IsChapter; public EStatus Status;
        public AudioClip Clip;
        public bool Silent;
        public override ENotificationIconType Icon => ENotificationIconType.Quest;
        public override Color? BackgroundColor => _fallback ? null : Color.white;
        /// <summary>排队，和正式版一样一条一条出：游戏的 NotifierView 对「立刻显示」的通知是直接叠上去，
        /// 对排队的一次只处理一条、上一条完全收起才放下一条（09-23 SORA：几条同时来不许叠成一堆）。</summary>
        public override bool ShowImmediately => false;
        bool _fallback;

        public override BaseNotificationView CreateView(INotificationViewFactory viewFactory)
        {
            var notifier = viewFactory as NotifierView;
            GameObject go = null; MainQuestNotificationView view = null; var setup = false;
            try
            {
                var font = notifier != null && notifier._defaultNotificationTemplate != null ? notifier._defaultNotificationTemplate._text as TextMeshProUGUI : null;
                go = notifier != null ? ChapterBundle.Instantiate("MainQuestNotification", notifier._container, font) : null;
                view = go != null ? go.GetComponent<MainQuestNotificationView>() : null;
                if (view != null && view._icon != null && view._text != null)
                {
                    if (view._container == null) view._container = notifier._container;
                    notifier.SetupNotificationView(view); setup = true;
                    view.Init(this);
                    BannerHost.Attach(view, notifier);
                    return view;
                }
                Plugin.Log.LogWarning("[chapter/banner] 1.1 notification view unavailable, using default banner");
            }
            // 排队模式下这里一抛，游戏的通知队列会永远卡在「处理中」（ProcessQueuedNotifications 不清标志），连原生通知都不再显示——出错一律退回默认样式
            catch (System.Exception e) { Plugin.Log.LogWarning("[chapter/banner] 1.1 横幅创建失败，退回默认样式: " + e.Message); }
            if (setup) notifier.RemoveNotificationView(this, view);   // 已登记进通知栏的要正式撤掉（顺带销毁），光 Destroy 会留下一条空记录
            else if (go != null) Object.Destroy(go);
            _fallback = true;
            var fallback = viewFactory.CreateDefaultView(this);
            BannerHost.Attach(fallback, notifier);
            return fallback;
        }

        [HarmonyPatch(typeof(NotifierView), nameof(NotifierView.PlaySound))]
        public static class SoundPatch
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
