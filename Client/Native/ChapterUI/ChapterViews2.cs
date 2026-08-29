using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// ChapterViews.cs 的下半：目标行 / 日记 / 关联物品 / 未读徽章，以及两个 1.1 独有的小组件。
namespace VisitAPI.ChapterUI
{
    public class MainQuestTaskListView : MonoBehaviour
    {
        [SerializeField] public RectTransform _conditionsContainer;
        [SerializeField] public MainQuestTaskView _conditionsViewTemplate;
        [SerializeField] public GameObject _unreadWarning;
        [SerializeField] public HoverReadTrigger _hoverReadTrigger;
    }

    public class MainQuestTaskView : MonoBehaviour
    {
        [SerializeField] public Graphic _checkMarkBorder, _checkMark, _crossMark, _skipMark;
        [SerializeField] public TMP_Text _descriptionField, _titleField, _counterField;
        [SerializeField] public EFT.UI.QuestObjectiveView _conditionView;
        [SerializeField] public DialogButtonsContainer _dialogButtonsContainer;
        [SerializeField] public Color32 _activeColor, _finishedColor, _failedColor;
    }

    public class DialogButtonsContainer : MonoBehaviour
    {
        [SerializeField] public EFT.UI.DefaultUIButton _visitTraderButton, _radioButton, _visitOnLocationButton;
    }

    public class MainQuestNotesListView : MonoBehaviour
    {
        [SerializeField] public RectTransform _container;
        [SerializeField] public MainQuestNoteView _noteViewTemplate;
        [SerializeField] public EFT.UI.ScrollRectNoDrag _scroll;
    }

    public class MainQuestNoteView : MonoBehaviour
    {
        [SerializeField] public CanvasGroup _mainCanvasGroup, _unreadWarning;
        [SerializeField] public TMP_Text _text;
        [SerializeField] public MainQuestLinkedItemsListView _itemsView;
    }

    public class MainQuestLinkedItemsListView : MonoBehaviour
    {
        [SerializeField] public RectTransform _itemsContainer;
        [SerializeField] public MainQuestLinkedItemView _itemViewTemplate;
        [SerializeField] public GameObject _parentPanel;
    }

    public class MainQuestLinkedItemView : MonoBehaviour
    {
        [SerializeField] public RectTransform _itemIconContainer;
        [SerializeField] public Image _typeIcon;
        [SerializeField] public GameObject _unreadMarker;
    }

    public class MainQuestUnreadWarning : MonoBehaviour
    {
        [SerializeField] public List<GameObject> _hidableObjects;
        [SerializeField] public TMP_Text _counterField;
    }

    // 1.1 的悬停即读组件（prefab 里目标区/日记区本来就挂着）：ReadState.OnHover 给它接回调
    public class HoverReadTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public System.Action Enter;
        public void OnPointerEnter(PointerEventData e) => Enter?.Invoke();
        public void OnPointerExit(PointerEventData e) { }
    }

    // 1.1 的 MaxSizeLayoutGroup 是横向布局组的变种（0.16 没有）；PoC 先按普通横向布局组跑，上限逻辑之后照 dump 补
    public class MaxSizeLayoutGroup : HorizontalOrVerticalLayoutGroup
    {
        public override void CalculateLayoutInputHorizontal() { base.CalculateLayoutInputHorizontal(); CalcAlongAxis(0, false); }
        public override void CalculateLayoutInputVertical() { CalcAlongAxis(1, false); }
        public override void SetLayoutHorizontal() { SetChildrenAlongAxis(0, false); }
        public override void SetLayoutVertical() { SetChildrenAlongAxis(1, false); }
    }
}
