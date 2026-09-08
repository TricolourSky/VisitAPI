using EFT.Quests;
using UnityEngine;
using VisitAPI.Native;

namespace VisitAPI.ChapterUI
{
    /// <summary>G14 + B6/B7：任务/条件状态到「显示」的**唯一**判定出口。
    /// 旧版五处「失败」口径不一致（有的含 FailRestartable/Expired 有的不含）、两处状态节点名硬编码且命名家族不同
    /// （图标组叫 Active/Complete/Failed，横幅状态叫 Active/Succeeded/Failed/Unavailable——prefab 里烤死的，改不了名，
    /// 只能在这一张表里对齐）。以后任何"算不算失败/该亮哪个节点"一律问这里。</summary>
    public static class ChapterStates
    {
        /// UI 口径的「失败」：四种失败态一视同仁（EQuestStatus 全枚举里的失败家族一个不漏）
        public static bool Failed(EQuestStatus st) =>
            st == EQuestStatus.Fail || st == EQuestStatus.MarkedAsFailed || st == EQuestStatus.FailRestartable || st == EQuestStatus.Expired;

        /// 目标行的五态（1.1 MainQuestTaskView.EConditionStatus 同款）：
        /// 任务失败 → Failed；任务完成或该条件已达成 → Done；章节收尾了这条还没着落 → Skipped；其余 Active
        public enum ERow { Active, Done, Failed, Skipped }
        public static ERow Row(Quest quest, Condition cond, bool chapterOver)
        {
            if (Failed(quest.QuestStatus)) return ERow.Failed;
            if (quest.IsConditionDone(cond) || quest.QuestStatus == EQuestStatus.Success) return ERow.Done;
            return chapterOver ? ERow.Skipped : ERow.Active;
        }

        /// <summary>一行的活体快照：状态 + 计数进度。ChapterTasks.Style 排版和 ChapterLive 的「变了没」检测**同源**——
        /// 以前两处各算一遍同样的判定（体检第二轮收口），改计数口径只改这里。只有计数类目标（击杀 N/上交 N）有 x/y 和进度条。
        /// 09-07 对正式版 1.0.1 截图：**做完的计数目标照样带满格进度条和「250 000/250 000」**（灰字打勾那几行），不是做完就收掉。</summary>
        public struct RowSnap
        {
            public ERow State; public bool Counting; public int Cur; public float Fill;
            public string Key => $"{(int)State}|{Cur}";
        }

        public static RowSnap Snap(Quest quest, Condition cond, bool chapterOver)
        {
            var state = Row(quest, cond, chapterOver);
            var checker = quest.ProgressCheckers.TryGetValue(cond, out var pc) ? pc : null;
            // 09-08：1.1 标了 showCounter:false 的目标（「告诉 Ragman 侦察行动期间的发现」这类 GlobalVariableValue，value 是状态值不是计数）不画计数条
            var counting = checker != null && checker.HasGetter() && cond.value > 1 && (state == ERow.Active || state == ERow.Done)
                && !QuestFlags.HideCounter(quest.Id, cond.id.ToString());
            // 做完的行一律按满算：任务已交掉后计数器可能已经清零/丢失，读它会画出「0/250 000 + 打勾」的怪相
            var cur = !counting ? 0 : state == ERow.Done ? (int)cond.value : (int)checker.CurrentValue;
            return new RowSnap
            {
                State = state, Counting = counting, Cur = cur,
                Fill = counting ? Mathf.Clamp01(cur / cond.value) : 0f,
            };
        }

        /// 行标题颜色（G12：跳过态用第四色；bundle 旧 prefab 没存 _inactiveColor 时兜底 1.1 的灰）
        public static Color RowColor(MainQuestTaskView v, ERow row)
        {
            if (row == ERow.Done) return v._finishedColor;
            if (row == ERow.Failed) return v._failedColor;
            if (row != ERow.Skipped) return v._activeColor;
            Color32 c = v._inactiveColor;
            return c.a == 0 ? new Color32(150, 150, 150, 255) : (Color)c;
        }

        /// 章节图标组（BackgroundsNormel/BackgroundsSelected/StatusIcons）该亮的子节点名；iconsOnly=角标组没有激活态
        public static string IconNode(ChapterModel.State st, bool iconsOnly) =>
            st == ChapterModel.State.Succeeded ? "Complete" : st == ChapterModel.State.Failed ? "Failed" : iconsOnly ? "" : "Active";

        /// 横幅右上角状态块（Status 组）该亮的子节点名
        public static string BannerNode(ChapterModel.State st) =>
            st == ChapterModel.State.Succeeded ? "Succeeded" : st == ChapterModel.State.Failed ? "Failed" : st == ChapterModel.State.Active ? "Active" : "Unavailable";
    }
}
