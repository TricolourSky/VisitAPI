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
            if (quest.IsConditionDone(cond) || quest.QuestStatus == EQuestStatus.Success) return ERow.Done;
            return chapterOver ? ERow.Skipped : ERow.Active;
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
