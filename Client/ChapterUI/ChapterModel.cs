using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.Quests;
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

        public State Status
        {
            get
            {
                if (Quest.QuestStatus == EQuestStatus.Success) return State.Succeeded;
                if (ChapterStates.Failed(Quest.QuestStatus)) return State.Failed;
                return ChapterStates.Begun(Quest.QuestStatus) || Subs.Any(s => ChapterStates.Begun(s.QuestStatus)) ? State.Active : State.Unavailable;
            }
        }

        public IEnumerable<(Quest quest, Condition cond, bool primary)> Conditions(bool all = false)
        {
            var over = Status == State.Succeeded || Status == State.Failed;
            var active = Subs.Where(s => s.QuestStatus == EQuestStatus.Started || s.QuestStatus == EQuestStatus.AvailableForFinish).ToList();
            var prereq = new HashSet<string>(active.SelectMany(Prerequisites));
            IEnumerable<Quest> shown = all || over
                ? Subs.Where(s => ChapterStates.Begun(s.QuestStatus))
                : Subs.Where(s => active.Contains(s) || (s.QuestStatus == EQuestStatus.Success && prereq.Contains(s.Id)));
            foreach (var s in shown.Reverse())
            {
                if (!s.Template.Conditions.TryGetValue(EQuestStatus.AvailableForFinish, out var list)) continue;
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

        static IEnumerable<string> Prerequisites(Quest q) =>
            q.Template.Conditions.TryGetValue(EQuestStatus.AvailableForStart, out var cc)
                ? cc.OfType<ConditionQuest>().Select(c => c.target).Where(t => !string.IsNullOrEmpty(t))
                : Enumerable.Empty<string>();

        public IEnumerable<(string id, string text, Quest quest, List<(ELink type, string tpl, string raw)> links)> Notes()
        {
            foreach (var q in new[] { Quest }.Concat(Subs))
            {
                var e = QuestFlags.Get(q.Id); var notes = e?.Notes; if (notes == null) continue;
                var st = NoteStatus(q);
                if (ChapterStates.Begun(st) && notes.TryGetValue("Started", out var a)) yield return (a, a.Localized(), q, Links(e, a, q));
                if (ChapterStates.Begun(st) && q.Template.Conditions.TryGetValue(EQuestStatus.AvailableForFinish, out var cc))
                    foreach (var c in cc)
                        if (notes.TryGetValue("cond:" + c.id, out var n) && (st == EQuestStatus.Success || q.IsConditionDone(c)))
                            yield return (n, n.Localized(), q, Links(e, n, q));
                if (st == EQuestStatus.Success && notes.TryGetValue("Success", out var b)) yield return (b, b.Localized(), q, Links(e, b, q));
                if (ChapterStates.Failed(st) && notes.TryGetValue("Fail", out var c2)) yield return (c2, c2.Localized(), q, Links(e, c2, q));
            }
        }

        static List<(ELink type, string tpl, string raw)> Links(QuestFlags.Entry e, string noteId, Quest q)
        {
            var own = e.NoteLinks.TryGetValue(noteId, out var l) ? l.Select(x => (LinkType(x.Type), x.Tpl, "note:" + noteId + ":" + x.Tpl)) : Enumerable.Empty<(ELink, string, string)>();
            return own.Concat(Items(q)).GroupBy(x => x.Item3).Select(g => g.First()).ToList();
        }

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

        public IEnumerable<string> ReadableIds() =>
            Notes().Select(n => n.id).Concat(Conditions().Select(c => c.cond.id.ToString()))
                .Concat(Notes().SelectMany(n => n.links).Concat(ActiveItems()).Select(x => "item:" + x.raw)).Distinct();

        public static List<ChapterModel> All(QuestController qc)
        {
            var book = qc?.Quests; if (book == null) return new List<ChapterModel>();
            SkipState.Use(qc.Profile?.Id);
            var all = book.Where(q => q.Template != null && QuestFlags.IsChapter(q.Id)).Select(q => new ChapterModel
            {
                Quest = q,
                Subs = QuestFlags.SubsOf(q.Id).Select(book.GetConditional).Where(s => s?.Template != null).ToList()
            });
            var list = Plugin.ShowUnstarted.Value ? all : all.Where(c => c.Status != State.Unavailable);
            return list.Select((c, i) => (c, i))
                .OrderBy(x => UnlockedAt(x.c.Quest))
                .ThenBy(x => QuestFlags.Order(x.c.Quest.Id))
                .ThenBy(x => x.i)
                .Select(x => x.c).ToList();
        }

        static double UnlockedAt(Quest quest)
        {
            var stamps = quest?.StatusStartTimestamps;
            if (stamps == null || stamps.Count == 0) return double.MaxValue;
            var first = double.MaxValue;
            foreach (var kv in stamps) if (kv.Value > 0d && kv.Value < first) first = kv.Value;
            return first;
        }
    }
}
