using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Quests;
using EFT.UI;
using UnityEngine;
using UnityEngine.UI;

namespace VisitAPI.ChapterUI
{
    /// <summary>
    /// 章节屏逻辑（1.1 `MainQuestTabView.Show` 的我们版）：左列章节图标 → 选中章节 → 横幅/标题/状态块 → 主/可选目标行 → 日记 → 相关物品。
    /// 视图字段由 bundle 序列化直接连好；1.1 用 Odin 字典存的状态物件（Active/Complete/Failed…）按节点名找。DEV_NOTES #70。
    /// </summary>
    public partial class MainQuestTabView
    {
        readonly List<GameObject> _spawned = new();
        readonly List<(ChapterModel ch, MainQuestChapterIconView view)> _icons = new();
        List<ChapterModel> _chapters = new();
        ChapterModel _selected;
        QuestController _quests;

        public IEnumerable<ChapterModel> Chapters => _chapters;

        /// keepSelection：ChapterLive 自动重画时保住玩家选中的章节；打开任务页时照正式版落在激活的那一章
        public void Show(QuestController quests, bool keepSelection = false)
        {
            _quests = quests;
            foreach (var go in _objectsToActivate ?? new List<GameObject>()) go.SetActive(true);
            _chapters = ChapterModel.All(quests);
            if (_noTasksWarning != null) _noTasksWarning.SetActive(_chapters.Count == 0);
            Clear();
            foreach (var ch in _chapters) SpawnIcon(ch);
            var keep = keepSelection && _selected != null ? _chapters.FirstOrDefault(c => c.Quest.Id == _selected.Quest.Id) : null;
            Select(keep ?? _chapters.FirstOrDefault(c => c.Status == ChapterModel.State.Active) ?? _chapters.FirstOrDefault());
            if (_expandHistoryButton != null) { _expandHistoryButton.onClick.RemoveAllListeners(); _expandHistoryButton.onClick.AddListener(() => ToggleHistory()); }
            ChapterLive.Attach(this, quests);
            Plugin.Log.LogDebug($"[chapter] shown: {_chapters.Count} chapter(s)");
        }

        void Clear() { foreach (var go in _spawned) if (go != null) Destroy(go); _spawned.Clear(); _icons.Clear(); }

        void SpawnIcon(ChapterModel ch)
        {
            var list = _chaptersListView; if (list == null || list._iconTemplate == null) return;
            var view = Instantiate(list._iconTemplate, list._container, false); view.gameObject.SetActive(true); _spawned.Add(view.gameObject); _icons.Add((ch, view));
            ChapterImages.Apply(view._chapterIcon, ch.Icon);
            SetStatusObjects(view.transform.Find("BackgroundsNormel"), ch.Status, false);
            SetStatusObjects(view.transform.Find("BackgroundsSelected"), ch.Status, false);   // 选中态的金色底，按状态切子节点，整组由 Select 开关
            SetStatusObjects(view.transform.Find("StatusIcons"), ch.Status, true);
            Toggle(view.transform, "BackgroundsSelected", false);
            if (view._button != null) view._button.onClick.AddListener(() => { Click(); Select(ch); });
            SetWarning(view._unreadWarning, ReadState.AnyUnread(ch.ReadableIds()));
        }

        // 1.1 点章节图标的那一声（MainQuestIconClick = story_click，DEV_NOTES #73）
        static void Click() { var c = ChapterBundle.Clip("story_click"); if (c != null && Singleton<GUISounds>.Instantiated) Singleton<GUISounds>.Instance.PlaySound(c); }

        // 按名字开关一个子节点（找不到就当没有）；Unity 对象不用 ?.，写显式判断
        /// 完成的勾 / 失败的叉：prefab 里这两个节点是**满铺 100×100 且不保持比例**的，而 sprite 只有 30×27 ——
        /// 照抄就是一个被拉扁的巨大勾盖住整张章节图。1.1 是用代码摆的（dump 里没有方法体），
        /// 这里照它的成品复刻：原始像素大小 + 贴右下角内缩 4px，和隔壁 SelectedMarker（15×15 贴右上内缩 5）同一套做法。
        static void Badge(Transform child)
        {
            var img = child.GetComponent<Image>();
            if (img == null || img.sprite == null) return;
            img.SetNativeSize();
            var rt = child as RectTransform; if (rt == null) return;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-4f, 4f);
        }

        static void Toggle(Transform parent, string child, bool on) { var t = parent.Find(child); if (t != null) t.gameObject.SetActive(on); }

        // 图标里的状态物件叫 Active/Complete/Failed；只亮当前那个。StatusIcons 只有 Complete/Failed（激活态没有角标）
        static void SetStatusObjects(Transform group, ChapterModel.State st, bool iconsOnly)
        {
            if (group == null) return;
            var want = st == ChapterModel.State.Succeeded ? "Complete" : st == ChapterModel.State.Failed ? "Failed" : iconsOnly ? "" : "Active";
            for (var i = 0; i < group.childCount; i++)
            {
                var child = group.GetChild(i);
                var on = child.name == want;
                child.gameObject.SetActive(on);
                // 角标（完成的勾 / 失败的叉）在 prefab 里是**满铺 100×100 且不保持比例**的，
                // 而那两张 sprite 只有 30×27 —— 照抄就是一个被拉扁的巨大勾盖住整块图标。
                // 1.1 里它是按原始像素画的，所以这里补一句 SetNativeSize（底图那三张本来就是 100×100，不受影响）。
                if (on && iconsOnly) Badge(child);
            }
        }
    }
}
