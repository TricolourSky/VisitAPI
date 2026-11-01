using EFT.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VisitAPI.ChapterUI
{
    public class MainQuestNotificationView : BaseNotificationView
    {
        [SerializeField] public TextMeshProUGUI _title;
        [SerializeField] public Image _checkmarkIcon;
        [SerializeField] public Sprite _chapterBackgroundSprite, _subtaskBackgroundSprite, _checkmarkStartedSprite, _checkmarkSuccessSprite, _checkmarkFailSprite;
        public override bool ReturnToPool => false;

        public void Init(ChapterBanner n)
        {
            if (_background != null) _background.sprite = n.IsChapter ? _chapterBackgroundSprite : _subtaskBackgroundSprite;
            if (_checkmarkIcon != null) _checkmarkIcon.sprite = n.Status == ChapterBanner.EStatus.Success ? _checkmarkSuccessSprite : n.Status == ChapterBanner.EStatus.Fail ? _checkmarkFailSprite : _checkmarkStartedSprite;
            if (_title != null) TmpFix.Set(_title, n.Title ?? "");
            // 10-15 SORA：「1.2 的章节提醒字体是有颜色的」——状态行的底色就是原生 BaseNotificationView.Init 设的 _defaultTextColor（白），1.1 / 1.2 也一样；
            // 真正的颜色是 ChapterNotify 拼进 Text 里的富文本（章节开始绿 / 完成淡蓝 / 失败淡红），这里不用再碰 _text.color
            Init((EFT.Communications.Notification)n);
            if (_text != null) TmpFix.Set(_text, _text.text);
            if (n.Sprite != null && _icon != null) _icon.sprite = n.Sprite;
        }
    }
}
