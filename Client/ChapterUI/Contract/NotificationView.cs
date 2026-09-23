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
            Init((EFT.Communications.Notification)n);
            if (_text != null) TmpFix.Set(_text, _text.text);
            if (n.Sprite != null && _icon != null) _icon.sprite = n.Sprite;
        }
    }
}
