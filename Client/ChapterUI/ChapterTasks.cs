using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.Quests;
using UnityEngine;
using UnityEngine.UI;

namespace VisitAPI.ChapterUI
{
    /// <summary>目标行的活体记录：ChapterLive 每半秒拿它原位刷新（计数/进度/勾叉变色），不整屏重画。</summary>
    internal class RowTrack
    {
        public MainQuestTaskView Row; public Quest Quest; public Condition Cond; public bool Over; public string Indent; public string Last;
    }

    /// 章节屏·目标区（主/可选两栏）：五态记号、计数进度、G1 展开/收起、G10 目标小字、G11 嵌套子条件、逐行悬停即读。
    public partial class MainQuestTabView
    {
        internal readonly List<RowTrack> LiveRows = new();
        readonly List<string> _mainIds = new(), _optIds = new();
        // 默认收起 = 进行中的子任务 + 它们的直接前置（刚做完的那一步，灰字打勾）；EQ 展开 = 全史。规则见 ChapterModel.Conditions（09-07 照成品）。
        bool _expandedTasks;

        // 进度条/组队角标不在 1.1 的序列化字段表里（prefab 里烤死的裸节点），只能按路径抠。
        // 路径常量收拢在这一处，将来改 prefab 只对这里。
        const string RowInfoPath = "TaskInfo/QuestObjectiveTemplate/MainPart/Info";

        /// G1：1.1 的 _expandTasksButton 本来就躺在 prefab 里（旧版零引用）。收起=只看进行中，展开=连已收尾任务的目标一起看
        void WireExpandTasks()
        {
            if (_expandTasksButton == null) return;
            _expandTasksButton.onClick.RemoveAllListeners();
            _expandTasksButton.onClick.AddListener(() => { _expandedTasks = !_expandedTasks; if (_selected != null) { FillTasks(_selected); RefreshUnread(); } });
        }

        void FillTasks(ChapterModel ch)
        {
            LiveRows.Clear();
            var over = ch.Status == ChapterModel.State.Succeeded || ch.Status == ChapterModel.State.Failed;   // 章节已收尾：没着落的画减号
            var conds = ch.Conditions(_expandedTasks).ToList();
            // 有没有因收起而藏掉的行：全量与收起量不一样就有
            var hidden = !_expandedTasks && !over && ch.Conditions(true).Count() != conds.Count;
            FillList(_tasksView?._mainTasksList, conds.Where(c => c.primary), over, _mainIds);
            FillList(_tasksView?._optionalTasksList, conds.Where(c => !c.primary), over, _optIds);
            if (_tasksView?._optionalTasksList != null) _tasksView._optionalTasksList.gameObject.SetActive(_optIds.Count > 0);
            // 展开按钮只在真藏了东西、或已展开（要能收回去）时露出（1.1 Show 的 hasHiddenConditions 同款语义）
            var btn = _tasksView != null ? _tasksView._expandTasksButton : null;
            if (btn != null) btn.SetActive(hidden || _expandedTasks);
            if (_expandTasksButton != null) _expandTasksButton.gameObject.SetActive(hidden || _expandedTasks);
        }

        /// 哪些行上榜、主/可选怎么分、什么顺序，全在 ChapterModel.Conditions 里定（09-07 照成品重写）；这里只负责画。
        /// 子条件（带 parentId）在 1.1 成品里是平铺在可选目标栏、各带小字提示的独立行，不嵌套在父条件下面。
        void FillList(MainQuestTaskListView list, IEnumerable<(Quest quest, Condition cond, bool primary)> rows, bool chapterOver, List<string> ids)
        {
            ids.Clear();
            if (list == null || list._conditionsViewTemplate == null || list._conditionsContainer == null) return;
            var pool = ViewPool.For(list._conditionsContainer, list._conditionsViewTemplate.gameObject);
            pool.ReleaseAll();
            foreach (var (quest, cond, _) in rows)
            {
                ids.Add(cond.id.ToString());
                var row = SpawnRow(pool, quest, cond, chapterOver, null);
                ChapterDialogButton.Bind(row, quest, _quests);   // 「去找商人」（DEV_NOTES #71/#75/#86）
            }
            if (list._unreadWarning != null) list._unreadWarning.SetActive(ReadState.AnyUnread(ids));
        }

        MainQuestTaskView SpawnRow(ViewPool pool, Quest quest, Condition cond, bool chapterOver, string indent)
        {
            var row = pool.Acquire().GetComponent<MainQuestTaskView>();
            var track = new RowTrack { Row = row, Quest = quest, Cond = cond, Over = chapterOver, Indent = indent };
            track.Last = Style(row, quest, cond, chapterOver, indent);
            LiveRows.Add(track);
            if (indent != null && row._dialogButtonsContainer != null) row._dialogButtonsContainer.gameObject.SetActive(false);
            var id = cond.id.ToString();
            // B12：悬停即读挂在**行**上（行自身没 Graphic 时 ReadState 只给这一行补），不再整列表铺一张吃射线的隐形网
            ReadState.OnHover(row.gameObject, () => { ReadState.MarkRead(new[] { id }); RefreshUnread(); });
            return row;
        }

        /// 一行的全部样子（初次填充和 ChapterLive 原位刷新共用）。返回状态快照串，变了才需要重刷。
        internal string Style(MainQuestTaskView row, Quest quest, Condition cond, bool chapterOver, string indent = null)
        {
            var snap = ChapterStates.Snap(quest, cond, chapterOver);
            var state = snap.State;
            if (row._titleField != null)
            {
                var text = cond.FormattedDescription;
                if (state == ChapterStates.ERow.Failed) text = "(" + "UI/MainQuests/FailedTask".Localized() + ") " + text;
                TmpFix.Set(row._titleField, (indent ?? "") + text);
                row._titleField.color = ChapterStates.RowColor(row, state);
            }
            // G10：目标小字 = locale 键「<条件id>_hint」（1.1 原生，09-07 对正式版查实）或「<条件id> desc」（作者自己补的）；没写就不占地方
            if (row._descriptionField != null)
            {
                var key = cond.id + "_hint"; var desc = key.Localized();
                if (string.IsNullOrEmpty(desc) || desc == key) { key = cond.id + " desc"; desc = key.Localized(); }
                var has = indent == null && !string.IsNullOrEmpty(desc) && desc != key;
                row._descriptionField.gameObject.SetActive(has);
                if (has) TmpFix.Set(row._descriptionField, desc);
            }
            if (row._checkMark != null) row._checkMark.gameObject.SetActive(state == ChapterStates.ERow.Done);
            if (row._checkMarkBorder != null) row._checkMarkBorder.gameObject.SetActive(state == ChapterStates.ERow.Active || state == ChapterStates.ERow.Done);
            if (row._crossMark != null) row._crossMark.gameObject.SetActive(state == ChapterStates.ERow.Failed);
            if (row._skipMark != null) row._skipMark.gameObject.SetActive(state == ChapterStates.ERow.Skipped);
            if (row._counterField != null) { TmpFix.Set(row._counterField, snap.Counting ? $"{Num(snap.Cur)}/{Num((int)cond.value)}" : ""); row._counterField.gameObject.SetActive(snap.Counting); }
            var info = row.transform.Find(RowInfoPath);
            var bar = info != null ? info.Find("Progress") : null;
            if (bar != null)
            {
                bar.gameObject.SetActive(snap.Counting);
                var fillT = bar.Find("Image"); var fill = fillT != null ? fillT.GetComponent<Image>() : null;
                if (fill != null) fill.fillAmount = snap.Fill;
            }
            if (info != null) Toggle(info, "Group", false);   // 1.1 的"组队目标"角标，用不上
            return snap.Key;
        }

        /// 计数按正式版写法千位空格分组：「250 000/250 000」（1.0.1 截图实证），不是「250000」也不是「250,000」
        static string Num(int n) => n.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', ' ');

        void RefreshTaskUnread()
        {
            var main = _tasksView?._mainTasksList; var opt = _tasksView?._optionalTasksList;
            if (main != null && main._unreadWarning != null) main._unreadWarning.SetActive(ReadState.AnyUnread(_mainIds));
            if (opt != null && opt._unreadWarning != null) opt._unreadWarning.SetActive(ReadState.AnyUnread(_optIds));
        }
    }
}
