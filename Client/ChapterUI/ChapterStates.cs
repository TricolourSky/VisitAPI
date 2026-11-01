using System.Linq;
using EFT.Quests;
using UnityEngine;
using VisitAPI.Native;

namespace VisitAPI.ChapterUI
{
    public static class ChapterStates
    {
        /// 失败类状态：服务端说的 Fail 在大厅里被引擎记成 MarkedAsFailed，可重开的失败是 FailRestartable（10-24 收拢，旧 B8）
        public static bool FailLike(EQuestStatus st) => st == EQuestStatus.Fail || st == EQuestStatus.MarkedAsFailed || st == EQuestStatus.FailRestartable;

        /// 章节页 / 自动链眼里的「败了」：失败类 + 过期
        public static bool Failed(EQuestStatus st) => FailLike(st) || st == EQuestStatus.Expired;

        /// 进行中：接了还没交（10-24 收拢，旧 B9）
        public static bool Running(EQuestStatus st) => st == EQuestStatus.Started || st == EQuestStatus.AvailableForFinish;

        public static bool Begun(EQuestStatus st) => Running(st) || st == EQuestStatus.Success || Failed(st);

        public enum ERow { Active, Done, Failed, Skipped }
        public static ERow Row(Quest quest, Condition cond, bool chapterOver)
        {
            if (Failed(quest.QuestStatus)) return ERow.Failed;
            var parent = Parent(quest, cond);
            if (parent != null)
            {
                var st = quest.QuestStatus;
                // 10-15（SORA 章节二「重返海岸线」：拿到 U 盘就不用再去找另一处线索，直接打勾）：条件上写了 doneWithParent 的可选子目标，
                // 父目标一完成就按完成显示，而不是 1.1 的「跳过」（红字无勾）；SkipState.Note 也按完成记，交掉以后还是勾
                var tick = QuestFlags.DoneWithParent(quest.Id, cond.id.ToString());
                if (Running(st))
                {
                    SkipState.Note(quest);
                    if (!DoneRaw(quest, cond) && DoneRaw(quest, parent)) return tick ? ERow.Done : ERow.Skipped;
                }
                else if (st == EQuestStatus.Success && !tick && SkipState.IsSkipped(cond.id.ToString())) return ERow.Skipped;
            }
            if (TalkPending(quest, cond)) return chapterOver ? ERow.Skipped : ERow.Active;
            if (quest.IsConditionDone(cond) || quest.QuestStatus == EQuestStatus.Success) return ERow.Done;
            return chapterOver ? ERow.Skipped : ERow.Active;
        }

        /// <summary>10-02（SORA：「和 SORA 探讨…」还没谈就打勾了）：只能对话收的任务常拿「某条前置任务已完成」当目标——大厅交任务服务端要验条件，
        /// 占位条件交不掉，所以用一条接下瞬间就满足的真条件，文案写的却是那场对话。这种目标在任务被对话收掉之前按未完成显示。
        /// 认法：只能对话收（dialogOnly）+ 目标是「任务 X 已完成」+ X 在这条任务开始之前就已经完成了（X 写在它的 startAfter / 可接条件里，或者时间戳更早）。
        /// 接下之后才完成的「完成任务 X」是真目标，照常打勾。</summary>
        internal static bool TalkPending(Quest quest, Condition cond)
        {
            if (cond is not ConditionQuest cq || string.IsNullOrEmpty(cq.target)) return false;
            if (!Running(quest.QuestStatus) || !QuestFlags.DialogOnly(quest.Id)) return false;
            try
            {
                if (QuestFlags.StartAfter(quest.Id)?.Contains(cq.target) == true) return true;
                if (quest.StartGates().Any(g => g == cq.target)) return true;
                var target = QuestOps.Resolve()?.Quests?.GetConditional(cq.target);
                return target != null && target.QuestStatus == EQuestStatus.Success
                    && target.StatusStartTimestamps.TryGetValue(EQuestStatus.Success, out var doneAt)
                    && quest.StatusStartTimestamps.TryGetValue(EQuestStatus.Started, out var startedAt)
                    && doneAt <= startedAt + 5;
            }
            catch { return false; }
        }

        internal static bool DoneRaw(Quest quest, Condition cond)
        {
            try
            {
                if (quest.CompletedConditions != null && quest.CompletedConditions.Contains(cond.id)) return true;
                return quest.ProgressCheckers.TryGetValue(cond, out var pc) && pc != null && pc.HasGetter() && pc.Test();
            }
            catch { return false; }
        }

        /// 子目标的父目标（同一张完成条件表里 id = ParentId 的那条）；没有就 null。SkipState.Note 也用它（10-24 合一，旧 B11）
        internal static Condition Parent(Quest quest, Condition cond)
        {
            if (cond == null || !cond.ParentId.HasValue) return null;
            foreach (var c in quest.Conds(EQuestStatus.AvailableForFinish)) if (c != null && c.id == cond.ParentId.Value) return c;
            return null;
        }

        public struct RowSnap
        {
            public ERow State; public bool Counting; public int Cur; public float Fill;
            /// 比较用：状态或计数变了才重画（10-24，旧 B23）
            public (ERow state, int cur) Key => (State, Cur);
        }

        /// 这一行现在该长什么样。探针（FindItemProbe）不在这里跑——它只在真正重画那一行时查一次（ChapterTasks.Style；10-24，旧 B19）
        public static RowSnap Snap(Quest quest, Condition cond, bool chapterOver)
        {
            var state = Row(quest, cond, chapterOver);
            var checker = quest.ProgressCheckers.TryGetValue(cond, out var pc) ? pc : null;
            var live = checker != null && checker.HasGetter();
            var hasCounter = live || (quest.QuestStatus == EQuestStatus.Success && (cond is ConditionCounterCreator || cond is ConditionItem));
            var counting = hasCounter && cond.value > 1 && (state == ERow.Active || state == ERow.Done || state == ERow.Skipped)
                && !QuestFlags.HideCounter(quest.Id, cond.id.ToString());
            var cur = !counting ? 0 : state == ERow.Done ? (int)cond.value : live ? (int)checker.CurrentValue : SkipState.Count(cond.id.ToString());
            return new RowSnap
            {
                State = state, Counting = counting, Cur = cur,
                Fill = counting ? Mathf.Clamp01(cur / cond.value) : 0f,
            };
        }

        public static Color RowColor(MainQuestTaskView v, ERow row)
        {
            if (row == ERow.Done) return v._finishedColor;
            if (row == ERow.Failed || row == ERow.Skipped) return v._failedColor;
            return v._activeColor;
        }

        public static string IconNode(ChapterModel.State st, bool iconsOnly) =>
            st == ChapterModel.State.Succeeded ? "Complete" : st == ChapterModel.State.Failed ? "Failed" : iconsOnly ? "" : "Active";

        public static string BannerNode(ChapterModel.State st) =>
            st == ChapterModel.State.Succeeded ? "Succeeded" : st == ChapterModel.State.Failed ? "Failed" : st == ChapterModel.State.Active ? "Active" : "Unavailable";

        /// 千分位（10-24 收拢，旧 B12）：计数器 "1 000/2 000" 用普通空格，奖励横幅照 1.1 用不断行空格 U+00A0——两处分隔符不同是有意的，显示效果不变
        public static string Thousands(int n, char separator) => n.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', separator);
    }
}
