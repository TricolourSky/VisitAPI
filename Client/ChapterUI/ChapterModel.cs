using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.Quests;
using VisitAPI.Native;

namespace VisitAPI.ChapterUI
{
    /// <summary>章节 = 一条标了 `visitapi.chapter` 的任务；它 AvailableForFinish 里的 Quest 条件就是子任务清单（1.1 同款结构）。
    /// 章节状态由子任务推导：章节任务 Success→完成；章节任务失败家族→失败；章节或任一子任务动过→激活；否则未开放。DEV_NOTES #70。</summary>
    public class ChapterModel
    {
        public enum State { Unavailable, Active, Succeeded, Failed }
        public enum ELink { Item, Craft, Offer }   // G5：1.1 QuestNoteLink.ELinkedItemType 同款三型
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
                if (ChapterStates.Failed(Quest.QuestStatus)) return State.Failed;
                return Quest.QuestStatus >= EQuestStatus.Started || Subs.Any(s => s.QuestStatus >= EQuestStatus.Started) ? State.Active : State.Unavailable;
            }
        }

        /// 已开始的子任务的完成条件：(子任务, 条件, 主目标?)。G19：主/可选 = 条件自己的 IsNecessary **且**该子任务在章节里是主线
        /// （章节 ConditionQuest 的 IsNecessary，flags 下发）——可选子任务的目标一律进可选栏，不再只看条件一半。
        /// 目标区的行（09-07 拿正式版 1.0.1/1.1 三张截图逐行对过，规则全部来自成品）：
        ///   ① **哪些子任务上榜**：进行中的；已完成但是某条进行中子任务的**直接前置**的（灰字打勾，"刚做完的那一步"）；
        ///      章节已收尾或 all=true（EQ 展开）时全部上榜。
        ///   ② **主/可选**：顶层条件进主要目标，带 parentId 的子条件平铺进可选目标（各自带小字提示），不再嵌套在父条件下面。
        ///   ③ **顺序**：新的在上——子任务按章节清单倒序，条件按原数组倒序。
        public IEnumerable<(Quest quest, Condition cond, bool primary)> Conditions(bool all = false)
        {
            var over = Status == State.Succeeded || Status == State.Failed;
            var active = Subs.Where(s => s.QuestStatus == EQuestStatus.Started || s.QuestStatus == EQuestStatus.AvailableForFinish).ToList();
            var prereq = new HashSet<string>(active.SelectMany(Prerequisites));
            IEnumerable<Quest> shown = all || over
                ? Subs.Where(s => s.QuestStatus >= EQuestStatus.Started)
                : Subs.Where(s => active.Contains(s) || (s.QuestStatus == EQuestStatus.Success && prereq.Contains(s.Id)));
            foreach (var s in shown.Reverse())
            {
                if (!s.Template.Conditions.TryGetValue(EQuestStatus.AvailableForFinish, out var list)) continue;
                // 09-07 实机：带 parentId 的子条件在模板的 AvailableForFinish 里是**平铺**的（和父条件并列），只按 ChildConditions 往下挂
                // 会让「搜集足够数量的卢布」「前往立交桥」在主要目标里再露一次。顶层 = 不是任何条件的子条件的那些。
                var children = new HashSet<Condition>(list.Where(c => c.ChildConditions != null).SelectMany(c => c.ChildConditions).Where(c => c != null));
                foreach (var c in list.Where(c => !children.Contains(c) && s.CheckVisibilityStatus(c)).Reverse())
                {
                    yield return (s, c, true);
                    if (c.ChildConditions == null) continue;
                    foreach (var child in c.ChildConditions.Reverse())
                        if (child != null && s.CheckVisibilityStatus(child)) yield return (s, child, false);
                }
            }
        }

        /// 一条子任务 AvailableForStart 里点名的前置任务 id（ConditionQuest.target）
        static IEnumerable<string> Prerequisites(Quest q) =>
            q.Template.Conditions.TryGetValue(EQuestStatus.AvailableForStart, out var cc)
                ? cc.OfType<ConditionQuest>().Select(c => c.target).Where(t => !string.IsNullOrEmpty(t))
                : Enumerable.Empty<string>();

        /// 已解锁的日记（locale 键 = noteId）：章节自己 + 各子任务，按 Started / 目标达成 / Success / Fail 解锁；带上是谁的日记和它挂的相关物品。
        /// 09-07：1.1 每条目标可带 questNoteId（flags 里 `cond:<条件id>`），**目标打勾那一刻**多一条日记——正式版「和 Ragman 交谈」勾上就出
        /// 「出乎我的意料，Ragman 相当随和…」，任务本身还没交。顺序 = Started → 各目标按模板顺序 → Success/Fail，和剧情推进一致。
        public IEnumerable<(string id, string text, Quest quest, List<(ELink type, string tpl, string raw)> links)> Notes()
        {
            foreach (var q in new[] { Quest }.Concat(Subs))
            {
                var e = QuestFlags.Get(q.Id); var notes = e?.Notes; if (notes == null) continue;
                var st = NoteStatus(q);
                if (st >= EQuestStatus.Started && notes.TryGetValue("Started", out var a)) yield return (a, a.Localized(), q, Links(e, a, q));
                if (st >= EQuestStatus.Started && q.Template.Conditions.TryGetValue(EQuestStatus.AvailableForFinish, out var cc))
                    foreach (var c in cc)
                        if (notes.TryGetValue("cond:" + c.id, out var n) && (st == EQuestStatus.Success || q.IsConditionDone(c)))
                            yield return (n, n.Localized(), q, Links(e, n, q));
                if (st == EQuestStatus.Success && notes.TryGetValue("Success", out var b)) yield return (b, b.Localized(), q, Links(e, b, q));
                if (ChapterStates.Failed(st) && notes.TryGetValue("Fail", out var c2)) yield return (c2, c2.Localized(), q, Links(e, c2, q));
            }
        }

        /// 一条日记下面挂的物品：1.1 日记表里这条日记自己的 links（flags noteLinks）+ 任务 JSON `visitapi.items` 明写的（作者便利，挂在该任务每条日记下）
        static List<(ELink type, string tpl, string raw)> Links(QuestFlags.Entry e, string noteId, Quest q)
        {
            var own = e.NoteLinks.TryGetValue(noteId, out var l) ? l.Select(x => (LinkType(x.Type), x.Tpl, "note:" + noteId + ":" + x.Tpl)) : Enumerable.Empty<(ELink, string, string)>();
            return own.Concat(Items(q)).GroupBy(x => x.Item3).Select(g => g.First()).ToList();
        }

        static ELink LinkType(string t) => t == "craft" ? ELink.Craft : t == "offer" ? ELink.Offer : ELink.Item;

        /// 日记解锁用的状态：章节任务自己用推导出的 Status（激活=Started，完成=Success，失败=Fail），子任务用真状态
        EQuestStatus NoteStatus(Quest q)
        {
            if (q != Quest) return q.QuestStatus;
            var st = Status;
            if (st == State.Succeeded) return EQuestStatus.Success;
            if (st == State.Failed) return EQuestStatus.Fail;
            return st == State.Active ? EQuestStatus.Started : EQuestStatus.AvailableForStart;
        }

        /// 一条任务的相关物品：只认 JSON 里 `visitapi.items` 明写的（可带 craft:/offer: 前缀标类型，G5）。raw 原样当已读 key。DEV_NOTES #71
        /// 09-07 拿正式版对过：**不再**从上交/找到类目标推物品——正式版「把现金交给 Therapist」的日记下面没有卢布图标，
        /// 相关物品全是作者在日记表上明写的（1.1 的 links，见 Notes/Links）。要挂物品就写 items 或 noteLinks。
        public static IEnumerable<(ELink type, string tpl, string raw)> Items(Quest q) =>
            (QuestFlags.Get(q.Id)?.Items ?? new List<string>()).Select(s =>
                s.StartsWith("craft:") ? (ELink.Craft, s.Substring(6), s) : s.StartsWith("offer:") ? (ELink.Offer, s.Substring(6), s) : (ELink.Item, s, s));

        /// 这一章现在还用得上的物品（1.1 的 GetActiveLinks）：章节/进行中子任务明写的 items
        /// + 已解锁日记里 quests 点名的任务还在进行中的那些 links（没点名的跟着日记所属任务算）
        public IEnumerable<(ELink type, string tpl, string raw)> ActiveItems()
        {
            var active = new HashSet<string>(Subs.Where(s => s.QuestStatus == EQuestStatus.Started || s.QuestStatus == EQuestStatus.AvailableForFinish).Select(s => s.Id)) { Quest.Id };
            var listed = Items(Quest).Concat(Subs.Where(s => active.Contains(s.Id)).SelectMany(Items));
            var fromNotes = Notes().SelectMany(n =>
            {
                var e = QuestFlags.Get(n.quest.Id);
                if (e == null || !e.NoteLinks.TryGetValue(n.id, out var l)) return Enumerable.Empty<(ELink, string, string)>();
                return l.Where(x => x.Quests.Count > 0 ? x.Quests.Any(active.Contains) : active.Contains(n.quest.Id))
                        .Select(x => (LinkType(x.Type), x.Tpl, "note:" + n.id + ":" + x.Tpl));
            });
            return listed.Concat(fromNotes).GroupBy(x => x.Item3).Select(g => g.First());
        }

        /// 这一章所有"可读"的 id：日记 noteId + 已开始子任务的条件 id + 相关物品（未读标记/计数用）
        public IEnumerable<string> ReadableIds() =>
            Notes().Select(n => n.id).Concat(Conditions().Select(c => c.cond.id.ToString()))
                .Concat(Notes().SelectMany(n => n.links).Concat(ActiveItems()).Select(x => "item:" + x.raw)).Distinct();

        public static List<ChapterModel> All(QuestController qc)
        {
            var book = qc?.Quests; if (book == null) return new List<ChapterModel>();
            var all = book.Where(q => q.Template != null && QuestFlags.IsChapter(q.Id)).Select(q => new ChapterModel
            {
                Quest = q,
                Subs = QuestFlags.SubsOf(q.Id).Select(book.GetConditional).Where(s => s?.Template != null).ToList()
            });
            // 还没开始的章节默认不列（空壳章节看着像坏了还剧透章节名）；BepInEx 配置 ShowUnstartedChapters 可开。
            var list = Plugin.ShowUnstarted.Value ? all : all.Where(c => c.Status != State.Unavailable);
            // G20：显示顺序 = visitapi.order 小的在前，没标 order 的按任务书原序垫底
            return list.Select((c, i) => (c, i)).OrderBy(x => QuestFlags.Order(x.c.Quest.Id)).ThenBy(x => x.i).Select(x => x.c).ToList();
        }
    }
}
