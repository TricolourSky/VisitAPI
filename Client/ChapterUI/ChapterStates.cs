using System.Linq;
using EFT.Quests;
using UnityEngine;
using VisitAPI.Native;

namespace VisitAPI.ChapterUI
{
    public static class ChapterStates
    {
        public static bool Failed(EQuestStatus st) =>
            st == EQuestStatus.Fail || st == EQuestStatus.MarkedAsFailed || st == EQuestStatus.FailRestartable || st == EQuestStatus.Expired;

        public static bool Begun(EQuestStatus st) =>
            st == EQuestStatus.Started || st == EQuestStatus.AvailableForFinish || st == EQuestStatus.Success || Failed(st);

        public enum ERow { Active, Done, Failed, Skipped }
        public static ERow Row(Quest quest, Condition cond, bool chapterOver)
        {
            if (Failed(quest.QuestStatus)) return ERow.Failed;
            var parent = Parent(quest, cond);
            if (parent != null)
            {
                var st = quest.QuestStatus;
                if (st == EQuestStatus.Started || st == EQuestStatus.AvailableForFinish)
                {
                    SkipState.Note(quest);
                    if (!DoneRaw(quest, cond) && DoneRaw(quest, parent)) return ERow.Skipped;
                }
                else if (st == EQuestStatus.Success && SkipState.IsSkipped(cond.id.ToString())) return ERow.Skipped;
            }
            if (TalkPending(quest, cond)) return chapterOver ? ERow.Skipped : ERow.Active;
            if (quest.IsConditionDone(cond) || quest.QuestStatus == EQuestStatus.Success) return ERow.Done;
            return chapterOver ? ERow.Skipped : ERow.Active;
        }

        /// <summary>10-02（SORA：「和 SORA 探讨…」还没谈就打勾了）：只能对话收的任务常拿「某条前置任务已完成」当目标——大厅交任务服务端要验条件，
        /// 占位条件交不掉，所以用一条接下瞬间就满足的真条件，文案写的却是那场对话。这种目标在任务被对话收掉之前按未完成显示。
        /// 认法：只能对话收（dialogOnly）+ 目标是「任务 X 已完成」+ X 在这条任务开始之前就已经完成了（X 写在它的 startAfter / 可接条件里，或者时间戳更早）。
        /// 接下之后才完成的「完成任务 X」是真目标，照常打勾。</summary>
        static bool TalkPending(Quest quest, Condition cond)
        {
            if (!(cond is ConditionQuest cq) || string.IsNullOrEmpty(cq.target)) return false;
            var st = quest.QuestStatus;
            if ((st != EQuestStatus.Started && st != EQuestStatus.AvailableForFinish) || !QuestFlags.DialogOnly(quest.Id)) return false;
            try
            {
                if (QuestFlags.StartAfter(quest.Id)?.Contains(cq.target) == true) return true;
                if (quest.Template?.Conditions != null && quest.Template.Conditions.TryGetValue(EQuestStatus.AvailableForStart, out var gates) && gates != null
                    && gates.OfType<ConditionQuest>().Any(g => g.target == cq.target)) return true;
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

        static Condition Parent(Quest quest, Condition cond)
        {
            if (cond == null || !cond.ParentId.HasValue || quest?.Template?.Conditions == null) return null;
            if (!quest.Template.Conditions.TryGetValue(EQuestStatus.AvailableForFinish, out var list) || list == null) return null;
            foreach (var c in list) if (c != null && c.id == cond.ParentId.Value) return c;
            return null;
        }

        public struct RowSnap
        {
            public ERow State; public bool Counting; public int Cur; public float Fill;
            public string Key => $"{(int)State}|{Cur}";
        }

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
    }
}
