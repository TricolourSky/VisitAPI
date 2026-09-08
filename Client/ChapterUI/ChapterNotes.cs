using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VisitAPI.ChapterUI
{
    /// <summary>章节屏·日记区。正式版：默认只显示最新一条（短日记），点右上角展开切完整列表。
    /// G9：完整列表池化增量——本次会话里新解锁的日记淡入 + 滚动条自动落底；1.1 的 _scroll/_mainCanvasGroup 两个零引用槽位就此接上。</summary>
    public partial class MainQuestTabView
    {
        readonly Dictionary<string, HashSet<string>> _seenNotes = new();   // 章节id → 本会话已见过的日记id（判"哪条是新来的"）
        readonly List<(string id, MainQuestNoteView v)> _noteViews = new();   // 屏上活着的日记行：4 秒全读/悬停后集中刷绿标
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
            RefreshNoteUnread();   // 正式版：外层 `!` 要打开日记逐条看过才消，短日记本身不响应悬停
            var hist = _historyView; if (hist == null || hist._noteViewTemplate == null || hist._container == null) return;
            hist.gameObject.SetActive(_fullHistory);
            if (!_seenNotes.TryGetValue(ch.Quest.Id, out var seen)) { seen = _seenNotes[ch.Quest.Id] = new HashSet<string>(_noteIds); }   // 首见这一章：全部当旧的，不闪
            var pool = ViewPool.For(hist._container, hist._noteViewTemplate.gameObject);
            pool.ReleaseAll();
            _noteViews.Clear();
            var fresh = false;
            foreach (var (id, text, _, links) in notes)
            {
                var v = pool.Acquire().GetComponent<MainQuestNoteView>(); if (v == null) continue;
                _noteViews.Add((id, v));
                TmpFix.Set(v._text, text);
                FillLinks(v._itemsView, links);   // 每条日记下面挂它自己的相关物品（1.1 日记表 links + 任务明写的 items）
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
            yield return null;   // 等布局把新行排完
            if (scroll != null) scroll.verticalNormalizedPosition = 0f;
        }

        // 日记区的 `!` 全家：外层（带 G2 计数）+ 短日记的 + 每条日记行自己的，一律按 ReadState 现状刷
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
