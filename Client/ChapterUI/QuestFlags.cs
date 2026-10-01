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
        public bool Pack;   // 10-01：内容包里的任务——战局内交了，奖励由服务端在结算时统一发（见 RaidRewardHold）
        public List<string> AnyOfGroup;
        public bool Story;
        public string Icon;
        public List<string> StartAfter = new();   // 09-24：可以是一个或多个（任一完成即可开）——陨落星辰要么枪匠对话置位的桥接任务、要么踩到森林坠机
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
        public Mail MailInfo;
        public List<string> Finishers = new();
        /// 09-24：任务进入某状态时给档案变量赋值（1.1 的 GlobalVariable 奖励，0.16.9 不认那种奖励类型，包数据写成 visitapi.setVariables）：状态名 → 变量 id → 值
        public Dictionary<string, Dictionary<string, int>> SetVariables = new();
        /// 09-24：章节点名但本机没有定义的子任务（门票缺的 44 条），按章节顺序开任务时跳过它们
        public List<string> MissingSubs = new();
    }

    public class Mail { public string From, Entry, Dialogue, DialogueTrader, TextKey; }

    public static Mail MailOf(string id) => Get(id)?.MailInfo;

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
    public static bool Pack(string id) => Get(id)?.Pack == true;
    /// 09-26 SORA：1.1 原版章节一律按这张固定位次（id 取自 1.1 服务端 getMainQuestsList 下发的章节列表），包数据里的 visitapi.order 对它们不起作用；
    /// 只有自制章节能自己定位置：包里写 visitapi.order，没写就用配置项 CustomChapterOrder（默认 100，排在原版章节后面）。
    /// 神秘蓝焰（Blue Fire）SORA 09-26 这张表里没列，暂按 1.1 章节列表的顺序放在无名者和他们已经来了之间，待确认
    static readonly Dictionary<string, double> Official = new()
    {
        ["68cbd33676fe74b1e80bfd91"] = 1,     // 塔科夫之旅 Tour
        ["68cbcdc4c964ab83cc0c928e"] = 2,     // 陨落星辰 Falling Skies
        ["68da33fe00868edcb6025ac4"] = 3,     // 门票 The Ticket
        ["68da36cf7cff54fc6109874a"] = 4,     // Batya
        ["6900927ab7d28358f80b9421"] = 5,     // 无名者 The Unheard
        ["68e784b7fa3f1fa3770094ba"] = 5.5,   // 神秘蓝焰 Blue Fire（待定）
        ["6903d779fdfc4078740a4bd0"] = 6,     // 他们已经来了 They Are Already Here
        ["69052e18e680c2d3e3034d3a"] = 7,     // 意外证人 Accidental Witness
        ["68e3a35002661eb2d30ce387"] = 8,     // 探秘"迷宫" The Labyrinth
        ["69d38381cea4b428690ea1d9"] = 9,     // Boreas
    };

    public static double Order(string id)
    {
        if (id != null && Official.TryGetValue(id, out var rank)) return rank;
        var order = Get(id)?.Order ?? double.MaxValue;
        return order < double.MaxValue ? order : Plugin.CustomChapterOrder?.Value ?? 100;
    }
    /// 「哪些任务完成之一就能开」；没有就 null
    public static List<string> StartAfter(string id) { var s = Get(id)?.StartAfter; return s != null && s.Count > 0 ? s : null; }
    /// 这条任务是不是某章节的「起点」（写在该章节 startAfter 里）：起点允许在章节开门之前就自动接、也不算「章节已开始」的依据
    public static bool IsStarterOf(string chapterId, string questId) => chapterId != null && questId != null && Get(chapterId)?.StartAfter?.Contains(questId) == true;
    /// 任务进入 status（"Started" / "Success" / "Fail"）时要赋的变量；没有就 null
    public static Dictionary<string, int> VariablesOn(string questId, string status) => Get(questId)?.SetVariables.TryGetValue(status ?? "", out var m) == true && m.Count > 0 ? m : null;
    public static bool IsMissingSub(string chapterId, string questId) => chapterId != null && questId != null && Get(chapterId)?.MissingSubs.Contains(questId) == true;
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
        lock (_byQuest) return questId == null ? new List<string>() : _byQuest.Where(kv => kv.Value.StartAfter.Contains(questId)).Select(kv => kv.Key).ToList();
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
                catch (System.Exception ex) { bad++; Plugin.Log.LogWarning($"[flags] Failed to parse visitapi data for quest {p.Name}, skipping it: {ex.Message}"); }
            }
            lock (_byQuest)
            {
                _byQuest.Clear();
                foreach (var kv in parsed) _byQuest[kv.Key] = kv.Value;
            }
            if (bad > 0) Plugin.Log.LogWarning($"[flags] Flags for {bad} quest(s) could not be parsed (see above), the other {parsed.Count} were registered");
            var chapters = new List<(string id, Entry e)>();
            lock (_byQuest) chapters = _byQuest.Where(kv => kv.Value.Chapter && kv.Value.Subs.Count > 0).Select(kv => (kv.Key, kv.Value)).ToList();
            foreach (var (id, e) in chapters) Register(id, e.Subs.Keys.ToList());
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
            AutoStart = On(v, "autoStart"), AutoFinish = On(v, "autoFinish"), DialogOnly = On(v, "dialogOnly"), Story = On(v, "story"), Hidden = On(v, "hidden"), Pack = On(v, "pack"),
            Icon = v["icon"]?.Value<string>(), TalkTo = v["talkTo"]?.Value<string>(), CallTrader = v["call"]?.Value<string>(),
            Order = v["order"]?.Type == JTokenType.Integer || v["order"]?.Type == JTokenType.Float ? v["order"].Value<double>() : double.MaxValue
        };
        if (v["startAfter"] is JArray sa) e.StartAfter = sa.Select(x => x.Value<string>()).Where(s => !string.IsNullOrEmpty(s)).ToList();
        else if (v["startAfter"]?.Type == JTokenType.String && !string.IsNullOrEmpty(v["startAfter"].Value<string>())) e.StartAfter = new List<string> { v["startAfter"].Value<string>() };
        if (v["mail"] is JObject mail && mail["from"]?.Value<string>() is string from && from.Length == 24)
            e.MailInfo = new Mail
            {
                From = from, Entry = mail["entry"]?.Value<string>() ?? "InLobby", Dialogue = mail["dialogue"]?.Value<string>(),
                DialogueTrader = mail["dialogueTrader"]?.Value<string>() ?? from, TextKey = mail["text"]?.Value<string>()
            };
        if (v["anyOf"] is JArray grp) e.AnyOfGroup = grp.Select(x => x.Value<string>()).Where(s => !string.IsNullOrEmpty(s)).ToList();
        if (v["notes"] is JObject notes) foreach (var n in notes.Properties()) e.Notes[n.Name] = n.Value.Value<string>();
        if (v["items"] is JArray items) e.Items = items.Select(x => x.Value<string>()).Where(s => !string.IsNullOrEmpty(s)).ToList();
        if (v["subs"] is JObject subs) foreach (var s in subs.Properties()) e.Subs[s.Name] = s.Value.Value<bool>();
        if (v["noCounter"] is JArray nc) foreach (var x in nc) { var s = x.Value<string>(); if (!string.IsNullOrEmpty(s)) e.NoCounter.Add(s); }
        if (v["unlockDialogue"] is JArray ud) e.UnlockDialogue = ud.Select(x => x.Value<string>()).Where(s => !string.IsNullOrEmpty(s)).ToList();
        if (v["unlockLocations"] is JArray ul) e.UnlockLocations = ul.Select(x => x.Value<string>()).Where(s => !string.IsNullOrEmpty(s)).ToList();
        if (v["finishers"] is JArray fin) e.Finishers = fin.Select(x => x.Value<string>()).Where(s => !string.IsNullOrEmpty(s)).ToList();
        if (v["missingSubs"] is JArray miss) e.MissingSubs = miss.Select(x => x.Value<string>()).Where(s => !string.IsNullOrEmpty(s)).ToList();
        if (v["setVariables"] is JObject sv)
            foreach (var st in sv.Properties())
            {
                if (st.Value is not JObject vars) continue;
                var map = new Dictionary<string, int>();
                foreach (var p in vars.Properties())
                    if (p.Name.Length == 24 && (p.Value.Type == JTokenType.Integer || p.Value.Type == JTokenType.Float)) map[p.Name] = p.Value.Value<int>();
                if (map.Count > 0) e.SetVariables[st.Name] = map;
            }
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
