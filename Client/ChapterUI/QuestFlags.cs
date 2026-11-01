using System;
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
        public List<string> Subs = new();
        public Dictionary<string, List<NoteLink>> NoteLinks = new();
        public HashSet<string> NoCounter = new();
        public HashSet<string> DoneWithParent = new();   // 10-15：父目标完成时按完成打勾的可选子目标（目标条件上写 doneWithParent: true，见 ChapterStates.Row）
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
        /// 10-05：拿到其中任意一件物品就接这条任务（1.1 AutoStart 桶里「读到就开」的 CompletableItem，adapt 搬进 visitapi.startOnItems，见 ItemStart）
        public List<string> StartOnItems = new();
        /// 10-06：开章物品在 1.1 里谁能看到（物品 id → 规矩，见 ItemStart.Visible）
        public Dictionary<string, ItemStart.Rule> StartItemRules = new();
        /// 10-06：这一步完成时要弹的「剧情通关奖励：经验」（1.1 只弹完成奖励里标了 isImportant 的，0.16 读不到那个字段，服务端加好带下来）
        public double RewardXp;
    }

    public class Mail { public string From, Entry, Dialogue, DialogueTrader, TextKey; }

    public static Mail MailOf(string id) => Get(id)?.MailInfo;

    public static List<string> Finishers(string chapterId) => Get(chapterId)?.Finishers ?? new List<string>();

    public class NoteLink { public string Type, Tpl; public List<string> Quests = new(); }

    static readonly Dictionary<string, Entry> _byQuest = new();
    static readonly Dictionary<string, string> _chapterOf = new();
    static readonly Dictionary<string, List<string>> _subsOf = new();
    // 反向索引（10-24，旧 B26）：拉到 flags 时一次建好，查询只查表——StartedBy 每拾一件物品调一次、LocationGates 每张地图调一次
    static readonly Dictionary<string, List<string>> _startedBy = new(StringComparer.Ordinal);                  // 物品 tpl → 拿到就接的任务
    static readonly Dictionary<string, ItemStart.Rule> _startItemRule = new(StringComparer.Ordinal);           // 物品 tpl → 1.1 可见规矩（先登记的赢）
    static readonly Dictionary<string, List<string>> _locationGates = new(StringComparer.OrdinalIgnoreCase);    // 地图 id → 解锁它的任务
    static readonly Dictionary<string, List<string>> _dialogueGates = new(StringComparer.OrdinalIgnoreCase);    // 商人 id → 解锁对话的任务
    static readonly Dictionary<string, List<string>> _waitingOn = new(StringComparer.Ordinal);                  // 任务 id → startAfter 里点了它的任务
    static List<string> _chapterIds = new();
    static readonly List<string> None = new();   // 查不到时给的空表：只读，别往里加
    static List<string> OrNull(List<string> l) => l is { Count: > 0 } ? l : null;

    public static Entry Get(string questId) { lock (_byQuest) return _byQuest.TryGetValue(questId ?? "", out var e) ? e : null; }
    public static bool AnyOf(string id) => Get(id)?.AnyOf == true;
    public static List<string> AnyOfGroup(string id) => OrNull(Get(id)?.AnyOfGroup);
    public static bool IsChapter(string id) => Get(id)?.Chapter == true;
    public static bool AutoStart(string id) => Get(id)?.AutoStart == true;
    public static bool AutoFinish(string id) => Get(id)?.AutoFinish == true;
    public static bool DialogOnly(string id) => Get(id)?.DialogOnly == true;
    public static bool Pack(string id) => Get(id)?.Pack == true;
    public static double RewardXp(string id) => Get(id)?.RewardXp ?? 0;
    /// 09-26 SORA：1.1 原版章节一律按这张固定位次（id 取自 1.1 服务端 getMainQuestsList 下发的章节列表），包数据里的 visitapi.order 对它们不起作用；
    /// 只有自制章节能自己定位置：包里写 visitapi.order，没写就用配置项 CustomChapterOrder（默认 100，排在原版章节后面）。
    /// 神秘蓝焰（Blue Fire）按 #147（SORA 09-20 定的全解锁次序）放在无名者 5 和他们已经来了 6 之间（5.5）
    static readonly Dictionary<string, double> Official = new()
    {
        ["68cbd33676fe74b1e80bfd91"] = 1,     // 塔科夫之旅 Tour
        ["68cbcdc4c964ab83cc0c928e"] = 2,     // 陨落星辰 Falling Skies
        ["68da33fe00868edcb6025ac4"] = 3,     // 门票 The Ticket
        ["68da36cf7cff54fc6109874a"] = 4,     // Batya
        ["6900927ab7d28358f80b9421"] = 5,     // 无名者 The Unheard
        ["68e784b7fa3f1fa3770094ba"] = 5.5,   // 神秘蓝焰 Blue Fire（#147 的次序）
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
    public static List<string> StartAfter(string id) => OrNull(Get(id)?.StartAfter);
    /// 这条任务是不是某章节的「起点」（写在该章节 startAfter 里）：起点允许在章节开门之前就自动接、也不算「章节已开始」的依据
    public static bool IsStarterOf(string chapterId, string questId) => chapterId != null && questId != null && Get(chapterId)?.StartAfter?.Contains(questId) == true;
    /// 任务进入 status（"Started" / "Success" / "Fail"）时要赋的变量；没有就 null
    public static Dictionary<string, int> VariablesOn(string questId, string status) => Get(questId)?.SetVariables.TryGetValue(status ?? "", out var m) == true && m.Count > 0 ? m : null;
    public static bool IsMissingSub(string chapterId, string questId) => chapterId != null && questId != null && Get(chapterId)?.MissingSubs.Contains(questId) == true;
    /// 拿到其中任意一件就接的物品；没有就 null
    public static List<string> StartOnItems(string id) => OrNull(Get(id)?.StartOnItems);

    /// 拿到这件物品会接哪些任务（只读表）
    public static IReadOnlyList<string> StartedBy(string tpl)
    {
        lock (_byQuest) return !string.IsNullOrEmpty(tpl) && _startedBy.TryGetValue(tpl, out var l) ? l : None;
    }

    /// 开章物品的 1.1 可见规矩；不是开章物品（或包里没带规矩）就 null。同一件物品挂在章节和第一步上，规矩一样，取到一份即可
    public static ItemStart.Rule StartItemRule(string tpl)
    {
        lock (_byQuest) return !string.IsNullOrEmpty(tpl) && _startItemRule.TryGetValue(tpl, out var r) ? r : null;
    }
    public static string TalkTo(string id) => Get(id)?.TalkTo is { Length: > 0 } s ? s : null;
    public static string CallTrader(string id) => Get(id)?.CallTrader is { Length: > 0 } s ? s : null;

    /// 剧情解锁这张地图的任务（只读表）；没有就空
    public static IReadOnlyList<string> LocationGates(string locationId)
    {
        lock (_byQuest) return !string.IsNullOrEmpty(locationId) && _locationGates.TryGetValue(locationId, out var l) ? l : None;
    }

    public static bool HideCounter(string questId, string condId) => Get(questId)?.NoCounter.Contains(condId ?? "") == true;
    public static bool DoneWithParent(string questId, string condId) => Get(questId)?.DoneWithParent.Contains(condId ?? "") == true;

    public static bool? DialogueUnlocked(string traderId, QuestController qc)
    {
        if (string.IsNullOrEmpty(traderId)) return null;
        List<string> gates;
        lock (_byQuest) if (!_dialogueGates.TryGetValue(traderId, out gates)) return null;
        var book = qc?.Quests;
        if (book == null) return false;
        return gates.Any(id => book.GetConditional(id)?.QuestStatus == EQuestStatus.Success);
    }

    /// startAfter 里点名了这条任务的任务（只读表）；没有就空
    public static IReadOnlyList<string> WaitingOn(string questId)
    {
        lock (_byQuest) return questId != null && _waitingOn.TryGetValue(questId, out var l) ? l : None;
    }

    /// 全部章节的 id（只读表；10-24，旧 B27）
    public static IReadOnlyList<string> ChapterIds() { lock (_byQuest) return _chapterIds; }

    public static string ChapterOf(string subId) { lock (_byQuest) return subId != null && _chapterOf.TryGetValue(subId, out var c) ? c : null; }
    public static List<string> SubsOf(string chapterId) { lock (_byQuest) return _subsOf.TryGetValue(chapterId ?? "", out var l) ? l : new List<string>(); }

    /// <summary>剧情任务：章节、章节里的步骤、包里标了 isStoryQuest 的；includeHidden 时隐藏任务（1.1 的 notDisplayedQuest，服务端下发成 hidden）也算。
    /// 10-24（旧 B13）：QuestNotify.Story 用的是不含 Hidden 的一种——隐藏任务没有横幅，差别保留（Sora_Chapter 里有 3 条这样的任务）</summary>
    public static bool IsStory(string id, bool includeHidden = true)
    {
        var e = Get(id);
        return (e != null && (e.Chapter || e.Story || (includeHidden && e.Hidden))) || ChapterOf(id) != null;
    }

    public static void MarkStory(Quest chapter)
    {
        if (chapter?.Template == null || !IsChapter(chapter.Id) || SubsOf(chapter.Id).Count > 0) return;
        var subs = chapter.Conds(EQuestStatus.AvailableForFinish).OfType<ConditionQuest>().Select(c => c.target).Where(t => !string.IsNullOrEmpty(t)).ToList();
        if (subs.Count == 0) return;
        Register(chapter.Id, subs);
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
        LockedTraderHide.Fetch();
        ChapterEvents.Raise();
    }));

    static bool TryParse(string body)
    {
        try
        {
            if (JObject.Parse(body)["data"] is not JObject data) return false;
            var parsed = new Dictionary<string, Entry>();
            var bad = 0;
            foreach (var p in data.Properties())
            {
                try { parsed[p.Name] = ParseEntry(p.Value); }
                catch (System.Exception ex) { bad++; Plugin.Log.LogWarning($"[flags] Failed to parse visitapi data for quest {p.Name}, skipping it: {ex.Message}"); }
            }
            List<(string id, Entry e)> chapters;
            lock (_byQuest)
            {
                _byQuest.Clear();
                foreach (var kv in parsed) _byQuest[kv.Key] = kv.Value;
                chapters = _byQuest.Where(kv => kv.Value.Chapter && kv.Value.Subs.Count > 0).Select(kv => (kv.Key, kv.Value)).ToList();
                Index();
            }
            if (bad > 0) Plugin.Log.LogWarning($"[flags] Flags for {bad} quest(s) could not be parsed (see above), the other {parsed.Count} were registered");
            foreach (var (id, e) in chapters) Register(id, e.Subs.ToList());
            foreach (var e in parsed.Values)
                if (!string.IsNullOrEmpty(e.Icon)) ChapterUI.ChapterImages.Preload(e.Icon);
            return true;
        }
        catch (System.Exception ex) { Plugin.Log.LogWarning("[flags] parse failed: " + ex.Message); return false; }
    }

    /// 在 _byQuest 的锁里调：按 _byQuest 的顺序重建全部反向索引
    static void Index()
    {
        _startedBy.Clear(); _startItemRule.Clear(); _locationGates.Clear(); _dialogueGates.Clear(); _waitingOn.Clear();
        var chapters = new List<string>();
        foreach (var kv in _byQuest)
        {
            var id = kv.Key; var e = kv.Value;
            if (e.Chapter) chapters.Add(id);
            foreach (var tpl in e.StartOnItems) Add(_startedBy, tpl, id);
            foreach (var rule in e.StartItemRules) if (!_startItemRule.ContainsKey(rule.Key)) _startItemRule[rule.Key] = rule.Value;
            foreach (var l in e.UnlockLocations) Add(_locationGates, l, id);
            foreach (var t in e.UnlockDialogue) Add(_dialogueGates, t, id);
            foreach (var s in e.StartAfter) Add(_waitingOn, s, id);
        }
        _chapterIds = chapters;
    }

    static void Add(Dictionary<string, List<string>> map, string key, string id)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (!map.TryGetValue(key, out var l)) map[key] = l = new List<string>();
        if (!l.Contains(id)) l.Add(id);
    }

    static Entry ParseEntry(JToken v)
    {
        var e = new Entry
        {
            AnyOf = On(v, "anyOf"), Chapter = On(v, "chapter"),
            AutoStart = On(v, "autoStart"), AutoFinish = On(v, "autoFinish"), DialogOnly = On(v, "dialogOnly"), Story = On(v, "story"), Hidden = On(v, "hidden"), Pack = On(v, "pack"),
            Icon = v["icon"]?.Value<string>(), TalkTo = v["talkTo"]?.Value<string>(), CallTrader = v["call"]?.Value<string>(),
            Order = Num(v, "order", double.MaxValue),
            RewardXp = Num(v, "rewardXp", 0)
        };
        if (v["startAfter"] is JArray sa) e.StartAfter = Strings(sa);
        else if (v["startAfter"]?.Type == JTokenType.String && v["startAfter"].Value<string>() is { Length: > 0 } one) e.StartAfter = new List<string> { one };
        if (v["mail"] is JObject mail && mail["from"]?.Value<string>() is string from && from.Length == 24)
            e.MailInfo = new Mail
            {
                From = from, Entry = mail["entry"]?.Value<string>() ?? "InLobby", Dialogue = mail["dialogue"]?.Value<string>(),
                DialogueTrader = mail["dialogueTrader"]?.Value<string>() ?? from, TextKey = mail["text"]?.Value<string>()
            };
        if (v["anyOf"] is JArray grp) e.AnyOfGroup = Strings(grp);
        if (v["notes"] is JObject notes) foreach (var n in notes.Properties()) e.Notes[n.Name] = n.Value.Value<string>();
        if (v["items"] is JArray items) e.Items = Strings(items);
        if (v["subs"] is JObject subs) e.Subs = subs.Properties().Select(p => p.Name).ToList();
        if (v["noCounter"] is JArray nc) e.NoCounter.UnionWith(Strings(nc));
        if (v["doneWithParent"] is JArray dwp) e.DoneWithParent.UnionWith(Strings(dwp));
        if (v["unlockDialogue"] is JArray ud) e.UnlockDialogue = Strings(ud);
        if (v["unlockLocations"] is JArray ul) e.UnlockLocations = Strings(ul);
        if (v["finishers"] is JArray fin) e.Finishers = Strings(fin);
        if (v["missingSubs"] is JArray miss) e.MissingSubs = Strings(miss);
        if (v["startOnItems"] is JArray soi) e.StartOnItems = Strings(soi);
        if (v["startItemRules"] is JObject sir)
            foreach (var p in sir.Properties())
                if (p.Value is JObject ro) e.StartItemRules[p.Name] = ItemStart.Rule.Parse(ro);
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
                        Quests = l["quests"] is JArray q ? Strings(q) : new List<string>()
                    }).Where(l => !string.IsNullOrEmpty(l.Tpl)).ToList();
        return e;
    }

    static bool On(JToken t, string name) => t[name]?.Type == JTokenType.Boolean && t[name].Value<bool>();
    static double Num(JToken t, string name, double fallback) => t[name]?.Type is JTokenType.Integer or JTokenType.Float ? t[name].Value<double>() : fallback;
    static List<string> Strings(JArray a) => a.Select(x => x.Value<string>()).Where(s => !string.IsNullOrEmpty(s)).ToList();
}
