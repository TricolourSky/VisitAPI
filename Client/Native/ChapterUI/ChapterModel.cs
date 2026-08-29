using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.Quests;
using VisitAPI.Native;

namespace VisitAPI.ChapterUI
{
    /// <summary>
    /// 章节 = 一条标了 `visitapi.chapter` 的任务；它 AvailableForFinish 里的 Quest 条件就是子任务清单（1.1 同款结构）。
    /// 章节状态由子任务推导：有失败→失败；章节任务 Success→完成；章节或任一子任务动过→激活；否则未开放。DEV_NOTES #70。
    /// </summary>
    public class ChapterModel
    {
        public enum State { Unavailable, Active, Succeeded, Failed }
        public Quest Quest;
        public List<Quest> Subs = new();
        public string Name => Quest.Template.Name;
        public string Banner => Quest.Template.Image;
        public string Icon => QuestFlags.Get(Quest.Id)?.Icon;

        public State Status
        {
            get
            {
                if (Quest.QuestStatus == EQuestStatus.Success) return State.Succeeded;
                // 章节的成败只看章节任务自己。**中途某条子任务失败不算整章失败** ——
                // 1.1 实证：「塔科夫之旅」里有一条「(已失败)…」红叉目标，章节右上角照样是「完成」。
                // 想让一章真的失败，就给章节任务自己写 Fail 条件、或在对话里 setstatus。
                if (Quest.QuestStatus == EQuestStatus.Fail || Quest.QuestStatus == EQuestStatus.MarkedAsFailed) return State.Failed;
                return Quest.QuestStatus >= EQuestStatus.Started || Subs.Any(s => s.QuestStatus >= EQuestStatus.Started) ? State.Active : State.Unavailable;
            }
        }

        /// 已开始的子任务的完成条件：(子任务, 条件, 主目标?)。isNecessary=false 的进可选栏
        public IEnumerable<(Quest quest, Condition cond, bool primary)> Conditions() =>
            Subs.Where(s => s.QuestStatus >= EQuestStatus.Started && s.Template.Conditions.ContainsKey(EQuestStatus.AvailableForFinish))
                .SelectMany(s => s.Template.Conditions[EQuestStatus.AvailableForFinish].Where(s.CheckVisibilityStatus).Select(c => (s, c, c.IsNecessary)));

        /// 已解锁的日记（locale 键 = noteId）：章节自己 + 各子任务，按 Started / Success / Fail 解锁；带上是谁的日记（相关物品挂它下面）。
        /// 章节任务的状态用推导出来的 Status（激活=Started，完成=Success，失败=Fail），子任务用真状态
        public IEnumerable<(string id, string text, Quest quest)> Notes()
        {
            foreach (var q in new[] { Quest }.Concat(Subs))
            {
                var notes = QuestFlags.Get(q.Id)?.Notes; if (notes == null) continue;
                var st = q == Quest ? Status == State.Succeeded ? EQuestStatus.Success : Status == State.Failed ? EQuestStatus.Fail : Status == State.Active ? EQuestStatus.Started : EQuestStatus.AvailableForStart : q.QuestStatus;
                if (st >= EQuestStatus.Started && notes.TryGetValue("Started", out var a)) yield return (a, a.Localized(), q);
                if (st == EQuestStatus.Success && notes.TryGetValue("Success", out var b)) yield return (b, b.Localized(), q);
                if ((st == EQuestStatus.Fail || st == EQuestStatus.MarkedAsFailed) && notes.TryGetValue("Fail", out var c)) yield return (c, c.Localized(), q);
            }
        }

        /// 一条任务的相关物品：JSON 里 `visitapi.items` 明写的 + 上交/找到类目标里的物品模板（作者不用再抄一遍）。DEV_NOTES #71
        public static IEnumerable<string> Items(Quest q) =>
            (QuestFlags.Get(q.Id)?.Items ?? new List<string>()).Concat(q.Template.Conditions.TryGetValue(EQuestStatus.AvailableForFinish, out var cc)
                ? cc.OfType<ConditionItem>().SelectMany(c => c.target ?? new string[0]) : Enumerable.Empty<string>()).Distinct();

        /// 这一章现在还用得上的物品（1.1 的 GetActiveLinks）：章节自己的 + 进行中（已接未交）子任务的
        public IEnumerable<string> ActiveItems() =>
            Items(Quest).Concat(Subs.Where(s => s.QuestStatus == EQuestStatus.Started || s.QuestStatus == EQuestStatus.AvailableForFinish).SelectMany(Items)).Distinct();

        /// 这一章所有"可读"的 id：日记 noteId + 已开始子任务的条件 id + 相关物品（未读标记用）
        public IEnumerable<string> ReadableIds() =>
            Notes().Select(n => n.id).Concat(Conditions().Select(c => c.cond.id.ToString())).Concat(ActiveItems().Select(t => "item:" + t));

        public static List<ChapterModel> All(QuestController qc)
        {
            var book = qc?.Quests; if (book == null) return new List<ChapterModel>();
            var all = book.Where(q => q.Template != null && QuestFlags.IsChapter(q.Id)).Select(q => new ChapterModel
            {
                Quest = q,
                Subs = q.Template.Conditions.TryGetValue(EQuestStatus.AvailableForFinish, out var cc)
                    ? cc.OfType<ConditionQuest>().Select(c => book.GetConditional(c.target)).Where(s => s != null && s.Template != null).ToList() : new List<Quest>()
            });
            // 还没开始的章节默认不列出来 —— 玩家在剧情页看到一个空壳章节（横幅在、目标区一片空白、标着「未开放」）
            // 只会以为坏了，顺带还剧透了后面的章节名。想看全貌的人可以在 BepInEx 配置里打开 ShowUnstartedChapters。
            return (Plugin.ShowUnstarted.Value ? all : all.Where(c => c.Status != State.Unavailable)).ToList();
        }
    }
}
