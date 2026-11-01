using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.Quests;
using UnityEngine;
using VisitAPI.Native;

namespace VisitAPI.ChapterUI
{
    public class ChapterModel
    {
        public enum State { Unavailable, Active, Succeeded, Failed }
        public enum ELink { Item, Craft, Offer }
        public Quest Quest;
        public List<Quest> Subs = new();
        public string Name => Quest.Template.Name;
        public string Banner => Quest.Template.Image;
        public string Icon => QuestFlags.Get(Quest.Id)?.Icon;

        State _status;
        int _statusFrame = -1;

        /// 章节状态。同一帧里算过就直接用（10-24，旧 B21：一次刷新里 Conditions / All / 图标 / 横幅要问十来遍，每遍都扫一遍子任务）；
        /// 状态真变了会有 ChapterEvents 让章节页整个重建，不会拿着旧值过帧
        public State Status
        {
            get
            {
                if (_statusFrame == Time.frameCount) return _status;
                _statusFrame = Time.frameCount;
                return _status = Compute();
            }
        }

        /// 章节已经收场（完成或失败）：没完成的目标按「跳过」画
        public bool Over => Status == State.Succeeded || Status == State.Failed;

        State Compute()
        {
            if (Quest.QuestStatus == EQuestStatus.Success) return State.Succeeded;
            if (ChapterStates.Failed(Quest.QuestStatus)) return State.Failed;
            return ChapterStates.Begun(Quest.QuestStatus) || Subs.Any(s => ChapterStates.Begun(s.QuestStatus)) ? State.Active : State.Unavailable;
        }

        public IEnumerable<(Quest quest, Condition cond, bool primary)> Conditions(bool all = false)
        {
            var over = Over;
            var active = Subs.Where(s => ChapterStates.Running(s.QuestStatus)).ToList();
            var prereq = new HashSet<string>(active.SelectMany(s => s.StartGates()));
            IEnumerable<Quest> shown = all || over
                ? Subs.Where(s => ChapterStates.Begun(s.QuestStatus))
                : Subs.Where(s => active.Contains(s) || (s.QuestStatus == EQuestStatus.Success && prereq.Contains(s.Id)));
            // 09-26（SORA：迷宫章节目标里露出两串 id）：1.1 标了 notDisplayedQuest 的子任务（服务端下发成 hidden，比如迷宫的 68ffdaf2——
            // 两条「某任务完成」、没有文案的收尾条件）1.1 界面上不显示，章节页也不列它的目标
            shown = shown.Where(s => QuestFlags.Get(s.Id)?.Hidden != true);
            foreach (var s in shown.Reverse())
            {
                var list = s.Conds(EQuestStatus.AvailableForFinish);
                var children = new HashSet<Condition>(list.Where(c => c.ChildConditions != null).SelectMany(c => c.ChildConditions).Where(c => c != null));
                foreach (var c in list.Where(c => !children.Contains(c)).Reverse())
                {
                    if (s.CheckVisibilityStatus(c)) yield return (s, c, true);
                    if (c.ChildConditions == null) continue;
                    foreach (var child in c.ChildConditions.Reverse())
                        if (child != null && s.CheckVisibilityStatus(child)) yield return (s, child, false);
                }
            }
        }

        public IEnumerable<(string id, string text, Quest quest, List<(ELink type, string tpl, string raw)> links)> Notes()
        {
            foreach (var q in new[] { Quest }.Concat(Subs))
            {
                var e = QuestFlags.Get(q.Id); var notes = e?.Notes; if (notes == null) continue;
                var st = NoteStatus(q);
                if (ChapterStates.Begun(st) && notes.TryGetValue("Started", out var a)) yield return (a, a.Localized(), q, Links(e, a, q));
                if (ChapterStates.Begun(st))
                    foreach (var c in q.Conds(EQuestStatus.AvailableForFinish))
                        if (notes.TryGetValue("cond:" + c.id, out var n) && (st == EQuestStatus.Success || q.IsConditionDone(c)))
                            yield return (n, n.Localized(), q, Links(e, n, q));
                if (st == EQuestStatus.Success && notes.TryGetValue("Success", out var b)) yield return (b, b.Localized(), q, Links(e, b, q));
                if (ChapterStates.Failed(st) && notes.TryGetValue("Fail", out var c2)) yield return (c2, c2.Localized(), q, Links(e, c2, q));
            }
        }

        static List<(ELink type, string tpl, string raw)> Links(QuestFlags.Entry e, string noteId, Quest q)
        {
            var own = e.NoteLinks.TryGetValue(noteId, out var l) ? l.Select(x => NoteItem(noteId, x)) : Enumerable.Empty<(ELink, string, string)>();
            return own.Concat(Items(q)).GroupBy(x => x.Item3).Select(g => g.First()).ToList();
        }

        static (ELink type, string tpl, string raw) NoteItem(string noteId, QuestFlags.NoteLink x) => (LinkType(x.Type), x.Tpl, "note:" + noteId + ":" + x.Tpl);

        static ELink LinkType(string t) => t == "craft" ? ELink.Craft : t == "offer" ? ELink.Offer : ELink.Item;

        EQuestStatus NoteStatus(Quest q)
        {
            if (q != Quest) return q.QuestStatus;
            var st = Status;
            if (st == State.Succeeded) return EQuestStatus.Success;
            if (st == State.Failed) return EQuestStatus.Fail;
            return st == State.Active ? EQuestStatus.Started : EQuestStatus.AvailableForStart;
        }

        public static IEnumerable<(ELink type, string tpl, string raw)> Items(Quest q) =>
            (QuestFlags.Get(q.Id)?.Items ?? new List<string>()).Select(s =>
                s.StartsWith("craft:") ? (ELink.Craft, s.Substring(6), s) : s.StartsWith("offer:") ? (ELink.Offer, s.Substring(6), s) : (ELink.Item, s, s));

        public IEnumerable<(ELink type, string tpl, string raw)> ActiveItems()
        {
            var active = new HashSet<string>(Subs.Where(s => ChapterStates.Running(s.QuestStatus)).Select(s => s.Id)) { Quest.Id };
            var listed = Items(Quest).Concat(Subs.Where(s => active.Contains(s.Id)).SelectMany(Items));
            var fromNotes = Notes().SelectMany(n =>
            {
                var e = QuestFlags.Get(n.quest.Id);
                if (e == null || !e.NoteLinks.TryGetValue(n.id, out var l)) return Enumerable.Empty<(ELink, string, string)>();
                return l.Where(x => x.Quests.Count > 0 ? x.Quests.Any(active.Contains) : active.Contains(n.quest.Id))
                        .Select(x => NoteItem(n.id, x));
            });
            return listed.Concat(fromNotes).GroupBy(x => x.Item3).Select(g => g.First());
        }

        /// 这一章「看过」要记的全部 id：日记、目标、日记里的关联物品、当前关联物品。Notes() 只算一遍（10-24，旧 B21）
        public IEnumerable<string> ReadableIds()
        {
            var notes = Notes().ToList();
            return notes.Select(n => n.id).Concat(Conditions().Select(c => c.cond.id.ToString()))
                .Concat(notes.SelectMany(n => n.links).Concat(ActiveItems()).Select(x => "item:" + x.raw)).Distinct();
        }

        public static List<ChapterModel> All(QuestController qc)
        {
            var book = qc?.Quests; if (book == null) return new List<ChapterModel>();
            SkipState.Use(qc.Profile?.Id);
            // 10-24（旧 B27）：章节 id 从 flags 的索引拿一次。
            // 仍按任务书的顺序走：位次相同的章节谁先谁后照旧由任务书顺序定，不能换成 flags 的顺序
            var chapterIds = new HashSet<string>(QuestFlags.ChapterIds());
            var all = book.Where(q => q?.Template != null && chapterIds.Contains(q.Id)).Select(q => new ChapterModel
            {
                Quest = q,
                Subs = QuestFlags.SubsOf(q.Id).Select(book.GetConditional).Where(s => s?.Template != null).ToList()
            });
            var list = Plugin.ShowUnstarted.Value ? all : all.Where(c => c.Status != State.Unavailable);
            // 09-26 SORA：不再按解锁先后排。原版 1.1 章节按 SORA 定的固定位次、自制章节按自己的 order（QuestFlags.Order）；
            // 章节开始了就自动排进它的位次，没开始的（只有打开「显示未开始的章节」才看得到）排在已开始的后面
            return list.OrderBy(c => c.Status == State.Unavailable ? 1 : 0).ThenBy(c => QuestFlags.Order(c.Quest.Id)).ToList();
        }
    }
}
