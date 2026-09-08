using EFT.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VisitAPI.ChapterUI
{
    /// <summary>1.1 的 MainQuestNotificationView 骨架（bundle 序列化契约，字段冻结规矩同 PanelViews.cs）：
    /// 基类是 0.16 真的 BaseNotificationView——立起/躺下动画、音效、排队、点击关闭全借它；
    /// 我们只管换底图（章节/子任务）、对勾（开始/完成/失败）、标题。DEV_NOTES #72。</summary>
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
            Init((EFT.Communications.Notification)n);   // 底盘：图标/正文/底色/动画速度/立起
            if (_text != null) TmpFix.Set(_text, _text.text);
            if (n.Sprite != null && _icon != null) _icon.sprite = n.Sprite;
        }
    }
}
