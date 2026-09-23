using System.Collections.Generic;
using System.Linq;
using EFT.Quests;
using Newtonsoft.Json.Linq;

namespace VisitAPI.Native;

public static class QuestFlags
{
    public class Entry
    {
        public bool AnyOf, Chapter, AutoStart, AutoFinish, DialogOnly;
        public bool Hidden;
        public List<string> AnyOfGroup;
        public bool Story;
        public string Icon, StartAfter;
        public double Order = double.MaxValue;
        public Dictionary<string, string> Notes = new();
        public List<string> Items = new();
        public Dictionary<string, bool> Subs = new();
        public Dictionary<string, List<NoteLink>> NoteLinks = new();
        public HashSet<string> NoCounter = new();
        public List<string> UnlockDialogue = new();
        public List<string> UnlockLocations = new();
        public string TalkTo;
        public string CallTrader;
        public List<string> Finishers = new();
    }

    public static List<string> Finishers(string chapterId) => Get(chapterId)?.Finishers ?? new List<string>();

    public class NoteLink { public string Type, Tpl; public List<string> Quests = new(); }

    static readonly Dictionary<string, Entry> _byQuest = new();
    static readonly Dictionary<string, string> _chapterOf = new();
    static readonly Dictionary<string, List<string>> _subsOf = new();

    public static Entry Get(string questId) { lock (_byQuest) return _byQuest.TryGetValue(questId ?? "", out var e) ? e : null; }
    public static bool AnyOf(string id) => Get(id)?.AnyOf == true;
    public static List<string> AnyOfGroup(string id) { var g = Get(id)?.AnyOfGroup; return g != null && g.Count > 0 ? g : null; }
    public static bool IsChapter(string id) => Get(id)?.Chapter == true;
    public static bool AutoStart(string id) => Get(id)?.AutoStart == true;
    public static bool AutoFinish(string id) => Get(id)?.AutoFinish == true;
    public static bool DialogOnly(string id) => Get(id)?.DialogOnly == true;
    public static double Order(string id)
    {
        var order = Get(id)?.Order ?? double.MaxValue;
        return order < double.MaxValue ? order : Plugin.CustomChapterOrder?.Value ?? 100;
    }
    public static string StartAfter(string id) { var s = Get(id)?.StartAfter; return string.IsNullOrEmpty(s) ? null : s; }
    public static string TalkTo(string id) { var s = Get(id)?.TalkTo; return string.IsNullOrEmpty(s) ? null : s; }
    public static string CallTrader(string id) { var s = Get(id)?.CallTrader; return string.IsNullOrEmpty(s) ? null : s; }

    public static List<string> LocationGates(string locationId)
    {
        lock (_byQuest)
            return string.IsNullOrEmpty(locationId) ? new List<string>()
                : _byQuest.Where(kv => kv.Value.UnlockLocations.Any(l => string.Equals(l, locationId, System.StringComparison.OrdinalIgnoreCase))).Select(kv => kv.Key).ToList();
    }

    public static bool HideCounter(string questId, string condId) => Get(questId)?.NoCounter.Contains(condId ?? "") == true;

    public static bool? DialogueUnlocked(string traderId, QuestController qc)
    {
        if (string.IsNullOrEmpty(traderId)) return null;
        List<string> gates;
        lock (_byQuest) gates = _byQuest.Where(kv => kv.Value.UnlockDialogue.Any(t => string.Equals(t, traderId, System.StringComparison.OrdinalIgnoreCase))).Select(kv => kv.Key).ToList();
        if (gates.Count == 0) return null;
        var book = qc?.Quests;
        if (book == null) return false;
        return gates.Any(id => book.GetConditional(id)?.QuestStatus == EQuestStatus.Success);
    }

    public static List<string> WaitingOn(string questId)
    {
        lock (_byQuest) return questId == null ? new List<string>() : _byQuest.Where(kv => kv.Value.StartAfter == questId).Select(kv => kv.Key).ToList();
    }

    public static string ChapterOf(string subId) { lock (_byQuest) return subId != null && _chapterOf.TryGetValue(subId, out var c) ? c : null; }
    public static List<string> SubsOf(string chapterId) { lock (_byQuest) return _subsOf.TryGetValue(chapterId ?? "", out var l) ? l : new List<string>(); }
    public static bool IsStory(string id) => IsChapter(id) || ChapterOf(id) != null || Get(id)?.Story == true || Get(id)?.Hidden == true;

    public static void MarkStory(Quest chapter)
    {
        if (chapter?.Template == null || !IsChapter(chapter.Id) || SubsOf(chapter.Id).Count > 0) return;
        if (!chapter.Template.Conditions.TryGetValue(EQuestStatus.AvailableForFinish, out var cc)) return;
        Register(chapter.Id, cc.OfType<ConditionQuest>().Select(c => c.target).Where(t => !string.IsNullOrEmpty(t)).ToList());
    }

    static void Register(string chapterId, List<string> subs)
    {
        lock (_byQuest) { _subsOf[chapterId] = subs; foreach (var s in subs) _chapterOf[s] = chapterId; }
    }

    public static void Prefetch() => Plugin.Instance.StartCoroutine(VisitHttp.Fetch("/visitapi/quest/flags", TryParse, "[flags]", ok =>
    {
        if (!ok) return;
        ChapterChain.Rescan();
        ChapterUI.ReadState.Sync();
        VariableGroups.Fetch();
        DialogConfirm.Fetch();
        ChapterEvents.Raise();
    }));

    static bool TryParse(string body)
    {
        try
        {
            if (!(JObject.Parse(body)["data"] is JObject data)) return false;
            var parsed = new Dictionary<string, Entry>();
            var bad = 0;
            foreach (var p in data.Properties())
            {
                try { parsed[p.Name] = ParseEntry(p.Value); }
                catch (System.Exception ex) { bad++; Plugin.Log.LogWarning($"[flags] 任务 {p.Name} 的 visitapi 数据解析失败，跳过这条: {ex.Message}"); }
            }
            lock (_byQuest)
            {
                _byQuest.Clear();
                foreach (var kv in parsed) _byQuest[kv.Key] = kv.Value;
            }
            if (bad > 0) Plugin.Log.LogWarning($"[flags] {bad} 条任务的 flags 没能解析（见上），其余 {parsed.Count} 条已登记");
            var chapters = new List<(string id, Entry e)>();
            lock (_byQuest) chapters = _byQuest.Where(kv => kv.Value.Chapter && kv.Value.Subs.Count > 0).Select(kv => (kv.Key, kv.Value)).ToList();
            foreach (var (id, e) in chapters) Register(id, e.Subs.Keys.ToList());
            Plugin.Log.LogDebug($"[flags] {_byQuest.Count} quest(s) with VisitAPI flags, {chapters.Count} chapter(s) indexed");
            lock (_byQuest)
                foreach (var e in _byQuest.Values)
                    if (!string.IsNullOrEmpty(e.Icon)) ChapterUI.ChapterImages.Preload(e.Icon);
            return true;
        }
        catch (System.Exception ex) { Plugin.Log.LogWarning("[flags] parse failed: " + ex.Message); return false; }
    }

    static Entry ParseEntry(JToken v)
    {
        var e = new Entry
        {
            AnyOf = On(v, "anyOf"), Chapter = On(v, "chapter"),
            AutoStart = On(v, "autoStart"), AutoFinish = On(v, "autoFinish"), DialogOnly = On(v, "dialogOnly"), Story = On(v, "story"), Hidden = On(v, "hidden"),
            Icon = v["icon"]?.Value<string>(), StartAfter = v["startAfter"]?.Value<string>(), TalkTo = v["talkTo"]?.Value<string>(), CallTrader = v["call"]?.Value<string>(),
            Order = v["order"]?.Type == JTokenType.Integer || v["order"]?.Type == JTokenType.Float ? v["order"].Value<double>() : double.MaxValue
        };
        if (v["anyOf"] is JArray grp) e.AnyOfGroup = grp.Select(x => x.Value<string>()).Where(s => !string.IsNullOrEmpty(s)).ToList();
        if (v["notes"] is JObject notes) foreach (var n in notes.Properties()) e.Notes[n.Name] = n.Value.Value<string>();
        if (v["items"] is JArray items) e.Items = items.Select(x => x.Value<string>()).Where(s => !string.IsNullOrEmpty(s)).ToList();
        if (v["subs"] is JObject subs) foreach (var s in subs.Properties()) e.Subs[s.Name] = s.Value.Value<bool>();
        if (v["noCounter"] is JArray nc) foreach (var x in nc) { var s = x.Value<string>(); if (!string.IsNullOrEmpty(s)) e.NoCounter.Add(s); }
        if (v["unlockDialogue"] is JArray ud) e.UnlockDialogue = ud.Select(x => x.Value<string>()).Where(s => !string.IsNullOrEmpty(s)).ToList();
        if (v["unlockLocations"] is JArray ul) e.UnlockLocations = ul.Select(x => x.Value<string>()).Where(s => !string.IsNullOrEmpty(s)).ToList();
        if (v["finishers"] is JArray fin) e.Finishers = fin.Select(x => x.Value<string>()).Where(s => !string.IsNullOrEmpty(s)).ToList();
        if (v["noteLinks"] is JObject links)
            foreach (var n in links.Properties())
                if (n.Value is JArray arr)
                    e.NoteLinks[n.Name] = arr.OfType<JObject>().Select(l => new NoteLink
                    {
                        Type = l["type"]?.Value<string>() ?? "item", Tpl = l["tpl"]?.Value<string>(),
                        Quests = (l["quests"] as JArray)?.Select(x => x.Value<string>()).Where(s => !string.IsNullOrEmpty(s)).ToList() ?? new List<string>()
                    }).Where(l => !string.IsNullOrEmpty(l.Tpl)).ToList();
        return e;
    }

    static bool On(JToken t, string name) => t[name]?.Type == JTokenType.Boolean && t[name].Value<bool>();
}
