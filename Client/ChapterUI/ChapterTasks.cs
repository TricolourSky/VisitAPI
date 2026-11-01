using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.Quests;
using UnityEngine;
using UnityEngine.UI;

namespace VisitAPI.ChapterUI
{
    internal class RowTrack
    {
        public MainQuestTaskView Row; public Quest Quest; public Condition Cond; public bool Over;
        public (ChapterStates.ERow state, int cur) Last;   // 上次画的是什么（10-24，旧 B23）
    }

    public partial class MainQuestTabView
    {
        internal readonly List<RowTrack> LiveRows = new();
        readonly List<string> _mainIds = new(), _optIds = new();
        bool _expandedTasks;

        const string RowInfoPath = "TaskInfo/QuestObjectiveTemplate/MainPart/Info";

        void WireExpandTasks()
        {
            if (_expandTasksButton == null) return;
            _expandTasksButton.onClick.RemoveAllListeners();
            _expandTasksButton.onClick.AddListener(() => { _expandedTasks = !_expandedTasks; if (_selected != null) { FillTasks(_selected); RefreshUnread(); } });
        }

        internal bool RowsStale()
        {
            try { return _selected != null && _selected.Conditions(_expandedTasks).Count() != LiveRows.Count; }
            catch { return false; }
        }

        void FillTasks(ChapterModel ch)
        {
            LiveRows.Clear();
            var over = ch.Over;
            var conds = ch.Conditions(_expandedTasks).ToList();
            var hidden = !_expandedTasks && !over && ch.Conditions(true).Count() != conds.Count;
            FillList(_tasksView?._mainTasksList, conds.Where(c => c.primary), over, _mainIds);
            FillList(_tasksView?._optionalTasksList, conds.Where(c => !c.primary), over, _optIds);
            if (_tasksView?._optionalTasksList != null) _tasksView._optionalTasksList.gameObject.SetActive(_optIds.Count > 0);
            var btn = _tasksView != null ? _tasksView._expandTasksButton : null;
            if (btn != null) btn.SetActive(hidden || _expandedTasks);
            if (_expandTasksButton != null) _expandTasksButton.gameObject.SetActive(hidden || _expandedTasks);
        }

        void FillList(MainQuestTaskListView list, IEnumerable<(Quest quest, Condition cond, bool primary)> rows, bool chapterOver, List<string> ids)
        {
            ids.Clear();
            if (list == null || list._conditionsViewTemplate == null || list._conditionsContainer == null) return;
            var pool = ViewPool.For(list._conditionsContainer, list._conditionsViewTemplate.gameObject);
            pool.ReleaseAll();
            foreach (var (quest, cond, primary) in rows)
            {
                ids.Add(cond.id.ToString());
                var row = SpawnRow(pool, quest, cond, chapterOver);
                Fit11(row, primary);
                ChapterDialogButton.Bind(row, quest, _quests, cond);
            }
            if (list._unreadWarning != null) list._unreadWarning.SetActive(ReadState.AnyUnread(ids));
        }

        MainQuestTaskView SpawnRow(ViewPool pool, Quest quest, Condition cond, bool chapterOver)
        {
            var row = pool.Acquire().GetComponent<MainQuestTaskView>();
            var track = new RowTrack { Row = row, Quest = quest, Cond = cond, Over = chapterOver };
            track.Last = Style(row, quest, cond, chapterOver);
            LiveRows.Add(track);
            HoverRead(row.gameObject, cond.id.ToString());
            return row;
        }

        /// 把一行画成它现在该有的样子，返回画的是什么（给 ChapterLive 比较）。snap 没给就现算；ChapterLive 比较过的快照直接传进来（10-24，旧 B23）。
        /// 探针（FindItemProbe）只在这里跑：行真的重画时查一次，不在每 0.5 秒的比较里扫仓库（10-24，旧 B19；探针本身 #183 定了留着）
        internal (ChapterStates.ERow state, int cur) Style(MainQuestTaskView row, Quest quest, Condition cond, bool chapterOver, ChapterStates.RowSnap? snap = null)
        {
            var s = snap ?? ChapterStates.Snap(quest, cond, chapterOver);
            var state = s.State;
            FindItemProbe.Check(quest, cond, state);
            if (row._titleField != null)
            {
                var text = cond.FormattedDescription;
                if (state == ChapterStates.ERow.Failed) text = "(" + "UI/MainQuests/FailedTask".Localized() + ") " + text;
                TmpFix.Set(row._titleField, text);
                row._titleField.color = ChapterStates.RowColor(row, state);
            }
            if (row._descriptionField != null)
            {
                var desc = Loc.Key(cond.id + "_hint") ?? Loc.Key(cond.id + " desc");
                row._descriptionField.gameObject.SetActive(desc != null);
                if (desc != null) TmpFix.Set(row._descriptionField, desc);
            }
            var skipped = state == ChapterStates.ERow.Skipped;
            if (row._checkMark != null) row._checkMark.gameObject.SetActive(state == ChapterStates.ERow.Done);
            var failed = state == ChapterStates.ERow.Failed;
            if (row._checkMarkBorder != null) row._checkMarkBorder.gameObject.SetActive(true);
            SkipVisual.Apply(row, skipped, failed);
            if (row._crossMark != null) row._crossMark.gameObject.SetActive(failed);
            if (row._skipMark != null) row._skipMark.gameObject.SetActive(false);
            if (row._counterField != null)
            {
                TmpFix.Set(row._counterField, s.Counting ? $"{ChapterStates.Thousands(s.Cur, ' ')}/{ChapterStates.Thousands((int)cond.value, ' ')}" : "");
                row._counterField.color = ChapterStates.RowColor(row, state);
                row._counterField.gameObject.SetActive(s.Counting);
            }
            var info = row.transform.Find(RowInfoPath);
            var bar = info != null ? info.Find("Progress") : null;
            if (bar != null)
            {
                bar.gameObject.SetActive(s.Counting);
                var fillT = bar.Find("Image"); var fill = fillT != null ? fillT.GetComponent<Image>() : null;
                if (fill != null) fill.fillAmount = s.Fill;
            }
            if (info != null) Toggle(info, "Group", false);
            return s.Key;
        }

        static class SkipVisual
        {
            class Tag : MonoBehaviour { public Color Orig; public bool Has; public Sprite CheckSprite; public Color CheckColor; public Image.Type CheckType; public bool CheckAspect, CheckHas; }
            static Sprite _bar;

            static Sprite Bar()
            {
                if (_bar != null) return _bar;
                const int n = 32;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[n * n];
                for (var y = 0; y < n; y++) for (var x = 0; x < n; x++)
                    px[y * n + x] = (x >= 8 && x < 24 && y >= 14 && y < 18) ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                tex.SetPixels32(px); tex.Apply(false, true);
                _bar = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
                _bar.name = "VisitAPI_SkipBar";
                return _bar;
            }

            public static void Apply(MainQuestTaskView row, bool on, bool failed)
            {
                var border = row._checkMarkBorder; if (border == null) return;
                var tag = border.GetComponent<Tag>() ?? border.gameObject.AddComponent<Tag>();
                if (!tag.Has) { tag.Orig = border.color; tag.Has = true; }
                var red = (Color)row._failedColor; if (red.a < 0.05f) red = new Color(0.75f, 0.15f, 0.15f, 1f);
                border.color = on || failed ? red : tag.Orig;
                if (row._checkMark is not Image check) return;
                if (!tag.CheckHas) { tag.CheckSprite = check.sprite; tag.CheckColor = check.color; tag.CheckType = check.type; tag.CheckAspect = check.preserveAspect; tag.CheckHas = true; }
                if (on)
                {
                    check.sprite = Bar(); check.type = Image.Type.Simple; check.preserveAspect = true; check.color = red;
                    check.gameObject.SetActive(true);
                }
                else
                {
                    check.sprite = tag.CheckSprite; check.type = tag.CheckType; check.preserveAspect = tag.CheckAspect; check.color = tag.CheckColor;
                }
            }
        }

        class RowMetrics : MonoBehaviour { public float BaseTitle = -1f; }
        static void Fit11(MainQuestTaskView row, bool primary)
        {
            var m = row.GetComponent<RowMetrics>() ?? row.gameObject.AddComponent<RowMetrics>();
            if (row._titleField != null)
            {
                if (m.BaseTitle < 0f) m.BaseTitle = row._titleField.fontSize;
                row._titleField.enableAutoSizing = false;
                row._titleField.fontSize = m.BaseTitle * (primary ? 0.925f : 0.82f);
                if (row._counterField != null) { row._counterField.enableAutoSizing = false; row._counterField.fontSize = row._titleField.fontSize; }
            }
            var markers = row._checkMarkBorder != null ? row._checkMarkBorder.transform.parent : null;
            if (markers != null && markers != row.transform) markers.localScale = new Vector3(0.81f, 0.81f, 1f);
        }

        void RefreshTaskUnread()
        {
            var main = _tasksView?._mainTasksList; var opt = _tasksView?._optionalTasksList;
            if (main != null && main._unreadWarning != null) main._unreadWarning.SetActive(ReadState.AnyUnread(_mainIds));
            if (opt != null && opt._unreadWarning != null) opt._unreadWarning.SetActive(ReadState.AnyUnread(_optIds));
        }
    }
}
