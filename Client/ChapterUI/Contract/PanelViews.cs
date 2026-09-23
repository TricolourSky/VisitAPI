using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VisitAPI.ChapterUI
{
    public partial class MainQuestTabView : MonoBehaviour
    {
        [SerializeField] public MainQuestChapterListView _chaptersListView;
        [SerializeField] public MainQuestChapterDescriptionView _chapterDescriptionView;
        [SerializeField] public MainQuestLinkedItemsListView _linkedItemsView;
        [SerializeField] public MainQuestNotesListView _historyView;
        [SerializeField] public MainQuestNoteView _shortHistoryView;
        [SerializeField] public MainQuestUnreadWarning _unreadHistoryWarning;
        [SerializeField] public Button _expandHistoryButton;
        [SerializeField] public MainQuestChapterTasksView _tasksView;
        [SerializeField] public GameObject _noTasksWarning;
        [SerializeField] public Button _expandTasksButton;
        [SerializeField] public List<GameObject> _objectsToActivate;
    }

    public class MainQuestChapterListView : MonoBehaviour
    {
        [SerializeField] public RectTransform _container;
        [SerializeField] public MainQuestChapterIconView _iconTemplate;
    }

    public class MainQuestChapterIconView : MonoBehaviour
    {
        [SerializeField] public Image _chapterIcon;
        [SerializeField] public MainQuestUnreadWarning _unreadWarning;
        [SerializeField] public GameObject _selectionObject;
        [SerializeField] public Button _button;
        [SerializeField] public EFT.UI.HoverTrigger _buttonHoverTrigger;
    }

    public class MainQuestChapterDescriptionView : MonoBehaviour
    {
        [SerializeField] public Image _image;
        [SerializeField] public TMP_Text _nameField;
    }

    public class MainQuestChapterTasksView : MonoBehaviour
    {
        [SerializeField] public MainQuestTaskListView _mainTasksList;
        [SerializeField] public MainQuestTaskListView _optionalTasksList;
        [SerializeField] public GameObject _expandTasksButton;
    }
}
