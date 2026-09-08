using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// ⚠️ bundle 序列化契约（下半：行级）。规矩同 PanelViews.cs：类名/字段名逐字冻结，只许加不许改。
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
        // 1.1 原槽位：0.16 的 QuestObjectiveView 序列化对不上，激活会 NRE（DEV_NOTES #86 同族雷），
        // 嵌套条件我们自己画（ChapterTasks 的子条件行），这个字段只接住引用、永不激活。
        [SerializeField] public EFT.UI.QuestObjectiveView _conditionView;
        [SerializeField] public DialogButtonsContainer _dialogButtonsContainer;
        [SerializeField] public Color32 _activeColor, _finishedColor, _failedColor;
        // G12：1.1 的第四色（跳过态）。bundle 里的旧 prefab 没存过它 → 反序列化成 (0,0,0,0)，
        // ChapterStates.SkippedColor 会兜底；将来重打 bundle 时连上即生效。
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

    // 1.1 的悬停即读组件（prefab 里目标区/日记区本来就挂着）：ReadState.OnHover 给它接回调。
    // G23：1.1 是悬停 0.1 秒才算读——鼠标划过不算，停住才算；移开就取消计时。
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

    // 1.1 的 MaxSizeLayoutGroup 是横向布局组的变种（0.16 没有）；按普通横向布局组跑（1.2.1 实机验证够用）
    public class MaxSizeLayoutGroup : HorizontalOrVerticalLayoutGroup
    {
        public override void CalculateLayoutInputHorizontal() { base.CalculateLayoutInputHorizontal(); CalcAlongAxis(0, false); }
        public override void CalculateLayoutInputVertical() { CalcAlongAxis(1, false); }
        public override void SetLayoutHorizontal() { SetChildrenAlongAxis(0, false); }
        public override void SetLayoutVertical() { SetChildrenAlongAxis(1, false); }
    }
}
