using EFT.Communications;
using EFT.UI;
using UnityEngine;
using UnityEngine.UI;
using VisitAPI.Native;

namespace VisitAPI.ChapterUI;

public class VisitBanner : NotificationWithText
{
    static readonly Vector4 Slice = new Vector4(4f, 8f, 33f, 8f);

    const string Bar = "banner.png";

    public override ENotificationIconType Icon => ENotificationIconType.Quest;

    /// <summary>和章节横幅一样排队（09-23 SORA：黑条也一起），见 ChapterBanner.ShowImmediately。</summary>
    public override bool ShowImmediately => false;

    public override Color? BackgroundColor => VisitArt.Load(Bar, Slice) != null ? new Color(1f, 1f, 1f, 200f / 255f) : (Color?)null;

    /// 09-26 F4：排队模式下 CreateView 一抛，游戏的通知队列会永远卡在「处理中」（见 ChapterBanner），我们加的换底图出错就用原生样式
    public override BaseNotificationView CreateView(INotificationViewFactory viewFactory)
    {
        var view = viewFactory.CreateDefaultView(this);
        try
        {
            BannerHost.Attach(view, viewFactory as NotifierView);
            var bar = VisitArt.Load(Bar, Slice);
            if (bar == null || view._background == null) return view;
            var keepSprite = view._background.sprite;
            var keepType = view._background.type;
            view._background.sprite = bar;
            view._background.type = Image.Type.Sliced;
            void Restore(Notification _, BaseNotificationView v)
            {
                v.OnHideComplete -= Restore;
                if (v._background == null) return;
                v._background.sprite = keepSprite;
                v._background.type = keepType;
            }
            view.OnHideComplete += Restore;
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("[banner] Failed to style the visit banner, showing the default one: " + e.Message); }
        return view;
    }
}
