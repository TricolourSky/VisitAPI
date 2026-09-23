using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

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
        [SerializeField] public Color32 _inactiveColor;
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

    public class HoverReadTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public System.Action Enter;
        Coroutine _pending;
        public void OnPointerEnter(PointerEventData e) { if (Enter != null && _pending == null) _pending = StartCoroutine(Later()); }
        public void OnPointerExit(PointerEventData e) { if (_pending != null) StopCoroutine(_pending); _pending = null; }
        void OnDisable() { _pending = null; }

        IEnumerator Later()
        {
            yield return new WaitForSecondsRealtime(0.1f);
            _pending = null;
            Enter?.Invoke();
        }
    }

    public class MaxSizeLayoutGroup : HorizontalOrVerticalLayoutGroup
    {
        public override void CalculateLayoutInputHorizontal() { base.CalculateLayoutInputHorizontal(); CalcAlongAxis(0, false); }
        public override void CalculateLayoutInputVertical() { CalcAlongAxis(1, false); }
        public override void SetLayoutHorizontal() { SetChildrenAlongAxis(0, false); }
        public override void SetLayoutVertical() { SetChildrenAlongAxis(1, false); }
    }
}
