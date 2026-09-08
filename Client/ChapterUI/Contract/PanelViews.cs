using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ⚠️ bundle 序列化契约（上半：面板级）。visitapi_chapterui.bundle 里的 prefab 记的是
// 「程序集 VisitAPI + 命名空间 VisitAPI.ChapterUI + 这些类名 + 这些字段名」，一个字都不能改，
// 改了序列化连线就断（SDK 侧的桩在旧仓库 tools/ChapterUI/extract_chapter_ui.py）。允许**加**字段（缺数据只是默认值）。
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
