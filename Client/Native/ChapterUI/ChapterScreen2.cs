using VisitAPI.Native;
using System.Linq;
using EFT;                 // "UI/MainQuests/FailedTask".Localized()
using EFT.Quests;
using UnityEngine;
using UnityEngine.UI;

namespace VisitAPI.ChapterUI
{
    /// ChapterScreen.cs 的下半：选中章节后填横幅/状态/目标/日记/相关物品
    public partial class MainQuestTabView
    {
        void Select(ChapterModel ch)
        {
            _selected = ch;
            foreach (var (model, view) in _icons)
            {
                var on = ch != null && model == ch;
                Toggle(view.transform, "SelectedMarker", on);
                Toggle(view.transform, "BackgroundsSelected", on);   // 正式版：选中章节换成金色底
                Toggle(view.transform, "BackgroundsNormel", !on);
            }
            // 一个章节都没有：把横幅/目标/日记/物品整块收起来，只留「没有进行中的剧情」那句话。
            // 不收的话，prefab 里的空壳横幅还挂在那儿，看着像坏了。
            foreach (var go in new[] { _chapterDescriptionView?.gameObject, _tasksView?.gameObject,
                                       _linkedItemsView?.gameObject, _shortHistoryView?.gameObject })
                if (go != null) go.SetActive(ch != null);
            if (ch == null) return;
            var desc = _chapterDescriptionView;
            if (desc != null)
            {
                TmpFix.Set(desc._nameField, ch.Name);
                ChapterImages.Apply(desc._image, ch.Banner);
                var status = desc.transform.Find("Status");
                var want = ch.Status == ChapterModel.State.Succeeded ? "Succeeded" : ch.Status == ChapterModel.State.Failed ? "Failed" : ch.Status == ChapterModel.State.Active ? "Active" : "Unavailable";
                if (status != null) for (var i = 0; i < status.childCount; i++) status.GetChild(i).gameObject.SetActive(status.GetChild(i).name == want);
            }
            FillTasks(ch); FillNotes(ch);
            ChapterLinks.Fill(_linkedItemsView, ch.ActiveItems(), _spawned, RefreshIconUnread);   // 底部「相关物品」= 这一章现在还用得上的物品
        }

        void FillTasks(ChapterModel ch)
        {
            var conds = ch.Conditions().ToList();
            var over = ch.Status == ChapterModel.State.Succeeded || ch.Status == ChapterModel.State.Failed;   // 章节已收尾：没做完也没失败的那几条画减号
            FillList(_tasksView?._mainTasksList, conds.Where(c => c.primary), over); FillList(_tasksView?._optionalTasksList, conds.Where(c => !c.primary), over);
            if (_tasksView?._optionalTasksList != null) _tasksView._optionalTasksList.gameObject.SetActive(conds.Any(c => !c.primary));
        }

        void FillList(MainQuestTaskListView list, System.Collections.Generic.IEnumerable<(Quest quest, Condition cond, bool primary)> rows, bool chapterOver = false)
        {
            if (list == null || list._conditionsViewTemplate == null || list._conditionsContainer == null) return;
            foreach (Transform old in list._conditionsContainer) if (old != list._conditionsViewTemplate.transform) Destroy(old.gameObject);
            var ids = rows.Select(r => r.cond.id.ToString()).ToList();
            if (list._unreadWarning != null) list._unreadWarning.SetActive(ReadState.AnyUnread(ids));
            ReadState.OnHover(list.gameObject, () => { ReadState.MarkRead(ids); if (list._unreadWarning != null) list._unreadWarning.SetActive(false); RefreshIconUnread(); });
            foreach (var (quest, cond, _) in rows)
            {
                var row = Instantiate(list._conditionsViewTemplate, list._conditionsContainer, false); row.gameObject.SetActive(true); _spawned.Add(row.gameObject);
                // 1.1 的 MainQuestTaskView 有五态（Active/Completed/Incomplete/Failed/Skipped），记号 prefab 里都在：
                // 任务失败 → 红叉 + 标题前缀「(已失败)」；任务完成 → 它的目标一律算完成（勾）；
                // 章节都收尾了这条还没着落 → 减号（正式版「确认前往实验室的路线」就是这个样子）。
                var qs = quest.QuestStatus;
                var failed = qs == EQuestStatus.Fail || qs == EQuestStatus.MarkedAsFailed;
                var done = !failed && (quest.IsConditionDone(cond) || qs == EQuestStatus.Success);
                var skipped = !done && !failed && chapterOver;
                if (row._titleField != null)
                {
                    var text = cond.FormattedDescription;
                    if (failed) text = "(" + "UI/MainQuests/FailedTask".Localized() + ") " + text;
                    TmpFix.Set(row._titleField, text);
                    row._titleField.color = done ? row._finishedColor : failed || skipped ? row._failedColor : row._activeColor;
                }
                if (row._descriptionField != null) row._descriptionField.gameObject.SetActive(false);   // 正式版这里是目标自己的一句话，不是任务简介；没有就留空
                if (row._checkMark != null) row._checkMark.gameObject.SetActive(done);
                if (row._checkMarkBorder != null) row._checkMarkBorder.gameObject.SetActive(!failed && !skipped);
                if (row._crossMark != null) row._crossMark.gameObject.SetActive(failed);
                if (row._skipMark != null) row._skipMark.gameObject.SetActive(skipped);
                var checker = quest.ProgressCheckers.TryGetValue(cond, out var pc) ? pc : null;
                var counting = checker != null && checker.HasGetter() && cond.value > 1 && !failed && !skipped;   // 只有计数类目标（击杀 N / 上交 N）才有进度条和 x/y
                if (row._counterField != null) { TmpFix.Set(row._counterField, counting ? $"{(int)checker.CurrentValue}/{(int)cond.value}" : ""); row._counterField.gameObject.SetActive(counting); }
                var info = row.transform.Find("TaskInfo/QuestObjectiveTemplate/MainPart/Info");
                var bar = info != null ? info.Find("Progress") : null;
                if (bar != null) { bar.gameObject.SetActive(counting); var fillT = bar.Find("Image"); var fill = fillT != null ? fillT.GetComponent<Image>() : null; if (fill != null) fill.fillAmount = counting ? Mathf.Clamp01((float)(checker.CurrentValue / cond.value)) : 0; }
                if (info != null) Toggle(info, "Group", false);   // 1.1 的"组队目标"角标，我们用不上
                ChapterDialog.Bind(row, quest, _quests);   // 「去找商人」「去现场」「去藏身处」（DEV_NOTES #71/#75）
            }
        }

        void FillNotes(ChapterModel ch)
        {
            var notes = ch.Notes().ToList();
            // 正式版：日记区默认只显示最新一条（短日记），完整列表收起，点右上角展开按钮才切换
            if (_shortHistoryView != null && _shortHistoryView._text != null)
            {
                _shortHistoryView.gameObject.SetActive(!_fullHistory && notes.Count > 0); TmpFix.Set(_shortHistoryView._text, notes.Count > 0 ? notes.Last().text : "");
                ChapterLinks.Fill(_shortHistoryView._itemsView, notes.Count > 0 ? ChapterModel.Items(notes.Last().quest) : null, _spawned, RefreshIconUnread);
            }
            var noteIds = notes.Select(n => n.id).ToList();
            RefreshNoteUnread(noteIds);   // 正式版：外层 `!` 要打开日记逐条看过才消，短日记本身不响应悬停
            var hist = _historyView; if (hist == null || hist._noteViewTemplate == null || hist._container == null) return;
            hist.gameObject.SetActive(_fullHistory);
            foreach (Transform old in hist._container) if (old != hist._noteViewTemplate.transform) Destroy(old.gameObject);
            foreach (var (id, text, quest) in notes)
            {
                var v = Instantiate(hist._noteViewTemplate, hist._container, false); v.gameObject.SetActive(true); _spawned.Add(v.gameObject);
                TmpFix.Set(v._text, text);
                ChapterLinks.Fill(v._itemsView, ChapterModel.Items(quest), _spawned, RefreshIconUnread);   // 每条日记下面挂它那条任务的物品
                if (v._unreadWarning != null) v._unreadWarning.alpha = ReadState.IsRead(id) ? 0 : 1;
                var view = v; ReadState.OnHover(v.gameObject, () => { ReadState.MarkRead(new[] { id }); if (view._unreadWarning != null) view._unreadWarning.alpha = 0; RefreshNoteUnread(noteIds); RefreshIconUnread(); });
            }
        }

        // 1.1 的 MainQuestUnreadWarning：_hidableObjects 是那枚绿色 `!`（计数字段先不用）
        static void SetWarning(MainQuestUnreadWarning w, bool on)
        {
            if (w == null) return;
            foreach (var go in w._hidableObjects ?? new System.Collections.Generic.List<GameObject>()) if (go != null) go.SetActive(on);
            if (w._counterField != null) w._counterField.gameObject.SetActive(false);
        }

        // 日记区外层的 `!`：短日记自带的 + 展开按钮旁的，只要还有没读的条目就亮
        void RefreshNoteUnread(System.Collections.Generic.List<string> noteIds)
        {
            var unread = ReadState.AnyUnread(noteIds);
            SetWarning(_unreadHistoryWarning, unread);
            if (_shortHistoryView != null && _shortHistoryView._unreadWarning != null) _shortHistoryView._unreadWarning.alpha = unread ? 1 : 0;
        }

        // 章节图标角标：这一章还有没读的日记/目标/物品就亮
        void RefreshIconUnread()
        {
            foreach (var (ch, view) in _icons) if (view != null) SetWarning(view._unreadWarning, ReadState.AnyUnread(ch.ReadableIds()));
        }

        bool _fullHistory;
        void ToggleHistory()
        {
            _fullHistory = !_fullHistory;
            if (_historyView != null) _historyView.gameObject.SetActive(_fullHistory);
            if (_shortHistoryView != null) _shortHistoryView.gameObject.SetActive(!_fullHistory && _selected != null && _selected.Notes().Any());
        }
    }
}
