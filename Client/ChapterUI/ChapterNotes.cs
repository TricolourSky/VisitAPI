using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VisitAPI.ChapterUI
{
    public partial class MainQuestTabView
    {
        readonly Dictionary<string, HashSet<string>> _seenNotes = new();
        readonly List<(string id, MainQuestNoteView v)> _noteViews = new();
        List<string> _noteIds = new();
        bool _fullHistory;

        void FillNotes(ChapterModel ch)
        {
            var notes = ch.Notes().ToList();
            _noteIds = notes.Select(n => n.id).ToList();
            if (_shortHistoryView != null && _shortHistoryView._text != null)
            {
                _shortHistoryView.gameObject.SetActive(!_fullHistory && notes.Count > 0);
                TmpFix.Set(_shortHistoryView._text, notes.Count > 0 ? notes.Last().text : "");
                FillLinks(_shortHistoryView._itemsView, notes.Count > 0 ? notes.Last().links : null);
            }
            RefreshNoteUnread();
            var hist = _historyView; if (hist == null || hist._noteViewTemplate == null || hist._container == null) return;
            hist.gameObject.SetActive(_fullHistory);
            if (!_seenNotes.TryGetValue(ch.Quest.Id, out var seen)) { seen = _seenNotes[ch.Quest.Id] = new HashSet<string>(_noteIds); }
            var pool = ViewPool.For(hist._container, hist._noteViewTemplate.gameObject);
            pool.ReleaseAll();
            _noteViews.Clear();
            var fresh = false;
            foreach (var (id, text, _, links) in notes)
            {
                var v = pool.Acquire().GetComponent<MainQuestNoteView>(); if (v == null) continue;
                _noteViews.Add((id, v));
                TmpFix.Set(v._text, text);
                FillLinks(v._itemsView, links);
                if (v._unreadWarning != null) v._unreadWarning.alpha = ReadState.IsRead(id) ? 0 : 1;
                var noteId = id;
                ReadState.OnHover(v.gameObject, () => { ReadState.MarkRead(new[] { noteId }); RefreshUnread(); });
                if (seen.Add(id)) { fresh = true; if (v._mainCanvasGroup != null) StartCoroutine(FadeIn(v._mainCanvasGroup)); }
                else if (v._mainCanvasGroup != null) v._mainCanvasGroup.alpha = 1f;
            }
            if (fresh && _fullHistory && hist._scroll != null) StartCoroutine(ScrollToEnd(hist._scroll));
        }

        static IEnumerator FadeIn(CanvasGroup g)
        {
            for (var t = 0f; t < 0.3f && g != null; t += Time.unscaledDeltaTime) { g.alpha = t / 0.3f; yield return null; }
            if (g != null) g.alpha = 1f;
        }

        static IEnumerator ScrollToEnd(UnityEngine.UI.ScrollRect scroll)
        {
            yield return null;
            if (scroll != null) scroll.verticalNormalizedPosition = 0f;
        }

        void RefreshNoteUnread()
        {
            var unread = ReadState.CountUnread(_noteIds);
            SetWarning(_unreadHistoryWarning, unread);
            if (_shortHistoryView != null && _shortHistoryView._unreadWarning != null) _shortHistoryView._unreadWarning.alpha = unread > 0 ? 1 : 0;
            foreach (var (id, v) in _noteViews) if (v != null && v._unreadWarning != null) v._unreadWarning.alpha = ReadState.IsRead(id) ? 0 : 1;
        }

        void ToggleHistory()
        {
            _fullHistory = !_fullHistory;
            if (_historyView != null) _historyView.gameObject.SetActive(_fullHistory);
            if (_shortHistoryView != null) _shortHistoryView.gameObject.SetActive(!_fullHistory && _selected != null && _noteIds.Count > 0);
            if (_fullHistory && _selected != null) { FillNotes(_selected); if (_historyView != null && _historyView._scroll != null) StartCoroutine(ScrollToEnd(_historyView._scroll)); }
        }
    }
}
