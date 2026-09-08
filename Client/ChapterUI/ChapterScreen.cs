using System.Collections;
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
    /// <summary>章节屏主逻辑（1.1 MainQuestTabView.Show 的我们版）：左列章节图标 → 选中章节 → 横幅/标题/状态块。
    /// 目标行在 ChapterTasks.cs、日记在 ChapterNotes.cs、相关物品在 ChapterLinks.cs（同一个 partial 类，按职责分文件）。
    /// 视图字段由 bundle 序列化连好；图标/行全走 ViewPool 复用（G8，B5 根治）。</summary>
    public partial class MainQuestTabView
    {
        readonly List<(ChapterModel ch, MainQuestChapterIconView view)> _icons = new();
        List<ChapterModel> _chapters = new();
        ChapterModel _selected;
        QuestController _quests;
        Coroutine _readAllCo;

        public IEnumerable<ChapterModel> Chapters => _chapters;

        /// keepSelection：ChapterLive 自动重画时保住玩家选中的章节；打开任务页时照正式版落在激活的那一章
        public void Show(QuestController quests, bool keepSelection = false)
        {
            _quests = quests;
            foreach (var go in _objectsToActivate ?? new List<GameObject>()) go.SetActive(true);
            _chapters = ChapterModel.All(quests);
            if (_noTasksWarning != null) _noTasksWarning.SetActive(_chapters.Count == 0);
            _icons.Clear();
            if (_chaptersListView != null && _chaptersListView._iconTemplate != null && _chaptersListView._container != null)
            {
                var pool = ViewPool.For(_chaptersListView._container, _chaptersListView._iconTemplate.gameObject);
                pool.ReleaseAll();
                foreach (var ch in _chapters) SpawnIcon(pool, ch);
            }
            var keep = keepSelection && _selected != null ? _chapters.FirstOrDefault(c => c.Quest.Id == _selected.Quest.Id) : null;
            Select(keep ?? _chapters.FirstOrDefault(c => c.Status == ChapterModel.State.Active) ?? _chapters.FirstOrDefault());
            if (_expandHistoryButton != null) { _expandHistoryButton.onClick.RemoveAllListeners(); _expandHistoryButton.onClick.AddListener(ToggleHistory); }
            WireExpandTasks();
            ChapterLive.Attach(this, quests);
            Plugin.Log.LogDebug($"[chapter] shown: {_chapters.Count} chapter(s)");
        }

        void SpawnIcon(ViewPool pool, ChapterModel ch)
        {
            var view = pool.Acquire().GetComponent<MainQuestChapterIconView>(); if (view == null) return;
            _icons.Add((ch, view));
            ChapterImages.Apply(view._chapterIcon, ch.Icon);
            SetStatusObjects(view.transform.Find("BackgroundsNormel"), ch.Status, false);
            SetStatusObjects(view.transform.Find("BackgroundsSelected"), ch.Status, false);   // 选中态金色底：按状态切子节点，整组由 Select 开关
            SetStatusObjects(view.transform.Find("StatusIcons"), ch.Status, true);
            Toggle(view.transform, "BackgroundsSelected", false);
            if (view._button != null) { view._button.onClick.RemoveAllListeners(); view._button.onClick.AddListener(() => { Click(); Select(ch); }); }
            // G13：悬停亮 1.1 的高亮件（HoverTrigger.Init 直接替换委托，池化复用不叠加）
            if (view._buttonHoverTrigger != null) view._buttonHoverTrigger.Init(_ => Hover(view, ch, true), _ => Hover(view, ch, false));
            SetWarning(view._unreadWarning, ReadState.CountUnread(ch.ReadableIds()));
        }

        void Hover(MainQuestChapterIconView view, ChapterModel ch, bool on)
        {
            if (_selected != null && ch.Quest.Id == _selected.Quest.Id) return;   // 选中章节的高亮归 Select 管
            if (view._selectionObject != null) view._selectionObject.SetActive(on);
            else Toggle(view.transform, "SelectedMarker", on);
        }

        void Select(ChapterModel ch)
        {
            _selected = ch;
            foreach (var (model, view) in _icons)
            {
                var on = ch != null && model == ch;
                Toggle(view.transform, "SelectedMarker", on);
                Toggle(view.transform, "BackgroundsSelected", on);   // 正式版：选中章节换金色底
                Toggle(view.transform, "BackgroundsNormel", !on);
                if (view._selectionObject != null) view._selectionObject.SetActive(on);
            }
            // 一个章节都没有：横幅/目标/日记/物品整块收起，只留「没有进行中的剧情」——空壳横幅挂着看着像坏了
            foreach (var go in new[] { _chapterDescriptionView?.gameObject, _tasksView?.gameObject,
                                       _linkedItemsView?.gameObject, _shortHistoryView?.gameObject })
                if (go != null) go.SetActive(ch != null);
            if (ch == null) { if (_historyView != null) _historyView.gameObject.SetActive(false); return; }
            var desc = _chapterDescriptionView;
            if (desc != null)
            {
                TmpFix.Set(desc._nameField, ch.Name);
                ChapterImages.Apply(desc._image, ch.Banner);
                var status = desc.transform.Find("Status");
                var want = ChapterStates.BannerNode(ch.Status);
                if (status != null) for (var i = 0; i < status.childCount; i++) status.GetChild(i).gameObject.SetActive(status.GetChild(i).name == want);
            }
            FillTasks(ch); FillNotes(ch);
            FillLinks(_linkedItemsView, ch.ActiveItems());   // 底部「相关物品」= 这一章现在还用得上的物品
            StartReadAll(ch);
        }

        /// G3：1.1 的 READ_ALL_DELAY_SEC=4——章节开着看满 4 秒，这一章所有未读自动清零
        void StartReadAll(ChapterModel ch)
        {
            if (_readAllCo != null) StopCoroutine(_readAllCo);
            _readAllCo = StartCoroutine(ReadAllLater(ch));
        }

        IEnumerator ReadAllLater(ChapterModel ch)
        {
            yield return new WaitForSecondsRealtime(4f);
            if (_selected != ch || !gameObject.activeInHierarchy) yield break;
            ReadState.MarkRead(ch.ReadableIds());
            RefreshUnread();
        }

        void OnDisable() { if (_readAllCo != null) { StopCoroutine(_readAllCo); _readAllCo = null; } }

        // 1.1 点章节图标的那一声（MainQuestIconClick = story_click，DEV_NOTES #73）
        static void Click() { var c = ChapterBundle.Clip("story_click"); if (c != null && Singleton<GUISounds>.Instantiated) Singleton<GUISounds>.Instance.PlaySound(c); }

        static void Toggle(Transform parent, string child, bool on) { var t = parent.Find(child); if (t != null) t.gameObject.SetActive(on); }

        // 图标里的状态物件按 ChapterStates 的表亮灯；StatusIcons 只有 Complete/Failed（激活态没有角标）
        static void SetStatusObjects(Transform group, ChapterModel.State st, bool iconsOnly)
        {
            if (group == null) return;
            var want = ChapterStates.IconNode(st, iconsOnly);
            for (var i = 0; i < group.childCount; i++)
            {
                var child = group.GetChild(i);
                var on = child.name == want;
                child.gameObject.SetActive(on);
                if (on && iconsOnly) Badge(child);
            }
        }

        /// 完成的勾/失败的叉：prefab 里这两个节点是满铺 100×100 不保比例的，而 sprite 只有 30×27——照抄就是被拉扁的巨勾。
        /// 1.1 是代码摆的（dump 无方法体），照成品复刻：原始像素 + 贴右下角内缩 4px（和 SelectedMarker 同一套做法）。
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

        // G2：1.1 的未读徽章带计数——绿 `!` 组 + 数字一起开关（旧版显式关掉了 _counterField，现在按 1.1 补回）。
        // internal：屏外的 UnreadBadge（底栏/页签）也用它驱动同一套件。
        internal static void SetWarning(MainQuestUnreadWarning w, int count)
        {
            if (w == null) return;
            foreach (var go in w._hidableObjects ?? new List<GameObject>()) if (go != null) go.SetActive(count > 0);
            if (w._counterField != null) { TmpFix.Set(w._counterField, count.ToString()); w._counterField.gameObject.SetActive(count > 0); }
        }

        /// 所有未读徽章按 ReadState 现状重算：章节图标 + 目标两栏 + 日记区 + 物品绿标（悬停/4 秒全读/重画后都走这里）
        void RefreshUnread()
        {
            foreach (var (ch, view) in _icons) if (view != null) SetWarning(view._unreadWarning, ReadState.CountUnread(ch.ReadableIds()));
            RefreshTaskUnread();
            RefreshNoteUnread();
            foreach (var kv in _linkMarkers) if (kv.Key != null) kv.Key.SetActive(!ReadState.IsRead(kv.Value));
        }
    }
}
