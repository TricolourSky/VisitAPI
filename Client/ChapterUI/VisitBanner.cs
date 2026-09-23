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

    public override BaseNotificationView CreateView(INotificationViewFactory viewFactory)
    {
        var view = viewFactory.CreateDefaultView(this);
        BannerHost.Attach(view, viewFactory as NotifierView);
        var bar = VisitArt.Load(Bar, Slice);
        if (bar == null) return view;
        var keepSprite = view._background.sprite;
        var keepType = view._background.type;
        view._background.sprite = bar;
        view._background.type = Image.Type.Sliced;
        void Restore(Notification _, BaseNotificationView v)
        {
            v.OnHideComplete -= Restore;
            v._background.sprite = keepSprite;
            v._background.type = keepType;
        }
        view.OnHideComplete += Restore;
        return view;
    }
}
