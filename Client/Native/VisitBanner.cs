using EFT.Communications;
using EFT.UI;
using UnityEngine;
using UnityEngine.UI;

namespace VisitAPI.Native;

// VisitAPI 自己的任务横幅：借原生通知底盘（BaseNotificationView 的立起/躺下动画 + 音效 + 排队），
// 只把底图换成从正式服截图里切出来的那条。**底图不调色**——正式服那条底色本来就是 (24,24,24)，
// 状态是靠文字颜色区分的；图标也不换，用游戏自己的任务通知图标，免得风格出戏。
public class VisitBanner : NotificationWithText
{
    // 底图整张水平翻转过：尖头在右边，左缘是干净直边 —— 图标和文字不用再让位，
    // 九宫格的不拉伸边也跟着换到右边（左 4 / 上下 8 / 右 33 = 尖头那一段）。
    static readonly Vector4 Slice = new Vector4(4f, 8f, 33f, 8f);

    const string Bar = "banner.png";

    public override ENotificationIconType Icon => ENotificationIconType.Quest;

    // 底图在就让它原样显示、只压半透明（alpha 跟原生默认通知一致 200/255）；art 缺失时留 null 走原生默认色
    public override Color? BackgroundColor => VisitArt.Load(Bar, Slice) != null ? new Color(1f, 1f, 1f, 200f / 255f) : (Color?)null;

    public override BaseNotificationView CreateView(INotificationViewFactory viewFactory)
    {
        var view = viewFactory.CreateDefaultView(this);
        var bar = VisitArt.Load(Bar, Slice);
        if (bar == null) return view;
        // 默认横幅是池化复用的（NotifierView 的 POOL_SIZE = 4）。Init 每次只重设 _icon.sprite 和
        // _background.color，底图和 Image.type 不重设 —— 不还原就会串到 SPT 自己的通知上。
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
