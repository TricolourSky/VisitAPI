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
    public partial class MainQuestTabView
    {
        readonly List<(ChapterModel ch, MainQuestChapterIconView view)> _icons = new();
        List<ChapterModel> _chapters = new();
        ChapterModel _selected;
        QuestController _quests;
        Coroutine _readAllCo;

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
            SetStatusObjects(view.transform.Find("BackgroundsSelected"), ch.Status, false);
            SetStatusObjects(view.transform.Find("StatusIcons"), ch.Status, true);
            Toggle(view.transform, "BackgroundsSelected", false);
            if (view._button != null) { view._button.onClick.RemoveAllListeners(); view._button.onClick.AddListener(() => { Click(); Select(ch); }); }
            if (view._buttonHoverTrigger != null) view._buttonHoverTrigger.Init(_ => Hover(view, ch, true), _ => Hover(view, ch, false));
            SetWarning(view._unreadWarning, ReadState.CountUnread(ch.ReadableIds()));
        }

        void Hover(MainQuestChapterIconView view, ChapterModel ch, bool on)
        {
            if (_selected != null && ch.Quest.Id == _selected.Quest.Id) return;
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
                Toggle(view.transform, "BackgroundsSelected", on);
                Toggle(view.transform, "BackgroundsNormel", !on);
                if (view._selectionObject != null) view._selectionObject.SetActive(on);
            }
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
            FillLinks(_linkedItemsView, ch.ActiveItems());
            StartReadAll(ch);
        }

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

        static void Click() { var c = ChapterBundle.Clip("story_click"); if (c != null && Singleton<GUISounds>.Instantiated) Singleton<GUISounds>.Instance.PlaySound(c); }

        static void Toggle(Transform parent, string child, bool on) { var t = parent.Find(child); if (t != null) t.gameObject.SetActive(on); }

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

        internal static void SetWarning(MainQuestUnreadWarning w, int count)
        {
            if (w == null) return;
            foreach (var go in w._hidableObjects ?? new List<GameObject>()) if (go != null) go.SetActive(count > 0);
            if (w._counterField != null) { TmpFix.Set(w._counterField, count.ToString()); w._counterField.gameObject.SetActive(count > 0); }
        }

        void RefreshUnread()
        {
            foreach (var (ch, view) in _icons) if (view != null) SetWarning(view._unreadWarning, ReadState.CountUnread(ch.ReadableIds()));
            RefreshTaskUnread();
            RefreshNoteUnread();
            foreach (var kv in _linkMarkers) if (kv.Key != null) kv.Key.SetActive(!ReadState.IsRead(kv.Value));
        }
    }
}
