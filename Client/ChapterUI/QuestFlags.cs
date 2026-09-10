using System.Collections.Generic;
using System.Linq;
using EFT.Quests;
using Newtonsoft.Json.Linq;

namespace VisitAPI.Native;

/// <summary>任务 JSON 里 VisitAPI 自己的开关（`visitapi.*` + 1.1 格式的 `notes`），启动时从 /visitapi/quest/flags 拉一次（没起来就隔 5 秒再试）。
/// anyOf=任一达成即可交（true）或「二选一组」的目标 id 数组（组内任一达成算组达成、组外照旧全要，09-10）；unlock=可提交时解锁商人；chapter=这条是章节；icon=章节图标 URL；notes=状态→日记 id；
/// autoStart/autoFinish=自动接/交（章节链）；dialogOnly=接交只能走对话；items=相关物品（G5 新增 `craft:`/`offer:` 前缀标类型）；
/// startAfter=自制前置；order=章节显示顺序（G20，小的在前）；subs=章节的子任务表 {子任务id: 是否主线}（服务端从任务模板直读，
/// **章节还锁着也照发** —— B14 的病根「Locked 时 _subsOf/_chapterOf 永远为空」从数据源上拔掉；G19 的主/可选也从这来）。</summary>
public static class QuestFlags
{
    public class Entry
    {
        public bool AnyOf, Unlock, Chapter, AutoStart, AutoFinish, DialogOnly;
        public List<string> AnyOfGroup;   // anyOf 写成数组时的「二选一组」（目标 id）；null = 老写法 true / 没开
        public bool Story;   // 1.1 任务自带的 isStoryQuest（服务端原样下发，09-07）：不进商人的普通任务列表
        public string Icon, StartAfter;
        public double Order = double.MaxValue;
        public Dictionary<string, string> Notes = new();   // Started/Success/Fail → 日记 id；`cond:<条件id>` → 该目标达成时解锁的日记（1.1 questNoteId，09-07）
        public List<string> Items = new();
        public Dictionary<string, bool> Subs = new();   // 子任务 id → IsNecessary（主线=true）
        public Dictionary<string, List<NoteLink>> NoteLinks = new();   // 日记 id → 这条日记挂的相关物品（1.1 日记表的 links，09-07）
        public HashSet<string> NoCounter = new();   // 1.1 `showCounter:false` 的条件 id：目标行不画计数/进度条（09-08）
        public List<string> UnlockDialogue = new(); // 这条任务完成后开放对话（访问）的商人（1.1 TraderDialogueUnlock 奖励的替身，09-08）
    }

    /// 1.1 日记表里一件相关物品：type = item/offer/craft，quests = 哪些任务进行中时算「现在还用得上」（GetActiveLinks 的口径）
    public class NoteLink { public string Type, Tpl; public List<string> Quests = new(); }

    static readonly Dictionary<string, Entry> _byQuest = new();
    static readonly Dictionary<string, string> _chapterOf = new();        // 子任务 → 章节
    static readonly Dictionary<string, List<string>> _subsOf = new();     // 章节 → 子任务

    public static Entry Get(string questId) { lock (_byQuest) return _byQuest.TryGetValue(questId ?? "", out var e) ? e : null; }
    public static bool AnyOf(string id) => Get(id)?.AnyOf == true;
    /// 二选一组（目标 id）；空组当没写，AnyOfQuest / AnyOfVisibility 据此走组规则
    public static List<string> AnyOfGroup(string id) { var g = Get(id)?.AnyOfGroup; return g != null && g.Count > 0 ? g : null; }
    public static bool Unlock(string id) => Get(id)?.Unlock == true;
    public static bool IsChapter(string id) => Get(id)?.Chapter == true;
    public static bool AutoStart(string id) => Get(id)?.AutoStart == true;
    public static bool AutoFinish(string id) => Get(id)?.AutoFinish == true;
    public static bool DialogOnly(string id) => Get(id)?.DialogOnly == true;
    public static double Order(string id) => Get(id)?.Order ?? double.MaxValue;
    /// 空串归一成 null：编辑器万一写出 "startAfter": ""，不归一的话 GetConditional("") 恒为 null，这条任务永远接不下且一声不吭
    public static string StartAfter(string id) { var s = Get(id)?.StartAfter; return string.IsNullOrEmpty(s) ? null : s; }
    /// G19：这条子任务在章节里算不算主线（章节 ConditionQuest 的 IsNecessary；没登记的按主线算）
    public static bool SubNecessary(string chapterId, string subId) =>
        !(Get(chapterId)?.Subs.TryGetValue(subId ?? "", out var n) == true) || n;

    /// 这条目标要不要画计数/进度条：1.1 标了 showCounter:false 的（「与 X 交谈」一族）不画（09-08）
    public static bool HideCounter(string questId, string condId) => Get(questId)?.NoCounter.Contains(condId ?? "") == true;

    /// <summary>商人的对话（访问）开放了没：null = 没有任何任务管这位商人（老行为，照开）；否则 = 点名了他的任务里有没有已完成的。
    /// 塔科夫之旅按 1.1 的 TraderDialogueUnlock 奖励逐步开放：Ragman/Therapist → Skier → Mechanic → Prapor（09-08，SORA 实机：以前一上来全开）。</summary>
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

    /// 哪些任务的 startAfter 指着这条任务（前置完成时由 ChapterChain 点名，09-07 终审）
    public static List<string> WaitingOn(string questId)
    {
        lock (_byQuest) return questId == null ? new List<string>() : _byQuest.Where(kv => kv.Value.StartAfter == questId).Select(kv => kv.Key).ToList();
    }

    public static string ChapterOf(string subId) { lock (_byQuest) return subId != null && _chapterOf.TryGetValue(subId, out var c) ? c : null; }
    public static List<string> SubsOf(string chapterId) { lock (_byQuest) return _subsOf.TryGetValue(chapterId ?? "", out var l) ? l : new List<string>(); }
    public static bool IsStory(string id) => IsChapter(id) || ChapterOf(id) != null || Get(id)?.Story == true;

    /// 章节表兜底登记（老路：从任务书里的章节模板推导）。flags 里带 subs 的章节以 flags 为准，不覆盖
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

    // 成功后 Rescan 补跑自动链（flags 迟到时登录那轮扫描是空跑的），再拉已读表；12 次全败不重试（服务端不在客户端根本登录不进，M9 已登记）
    public static void Prefetch() => Plugin.Instance.StartCoroutine(VisitHttp.Fetch("/visitapi/quest/flags", TryParse, "[flags]", ok =>
    {
        if (!ok) return;
        ChapterChain.Rescan();
        ChapterUI.ReadState.Sync();
        ChapterEvents.Raise();
    }));

    static bool TryParse(string body)
    {
        try
        {
            if (!(JObject.Parse(body)["data"] is JObject data)) return false;
            // 09-07 终审：以前整表一个 try——作者在某一条任务的 noteLinks / subs 里写错格式，整张表作废且 _byQuest 半写入，
            // 剧情系统（隐藏 / 横幅 / 自动链）静默失效。现在逐条解析，坏的那条跳过并点名，好的照登记；解析完再整体替换。
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
            foreach (var (id, e) in chapters) Register(id, e.Subs.Keys.ToList());   // B14：章节↔子任务表从 flags 直建，锁没锁都在
            Plugin.Log.LogDebug($"[flags] {_byQuest.Count} quest(s) with VisitAPI flags, {chapters.Count} chapter(s) indexed");
            lock (_byQuest)
                foreach (var e in _byQuest.Values)
                    if (!string.IsNullOrEmpty(e.Icon)) ChapterUI.ChapterImages.Preload(e.Icon);   // 图标先拉：横幅可能比剧情页先出现
            return true;
        }
        catch (System.Exception ex) { Plugin.Log.LogWarning("[flags] parse failed: " + ex.Message); return false; }
    }

    static Entry ParseEntry(JToken v)
    {
        var e = new Entry
        {
            AnyOf = On(v, "anyOf"), Unlock = On(v, "unlock"), Chapter = On(v, "chapter"),
            AutoStart = On(v, "autoStart"), AutoFinish = On(v, "autoFinish"), DialogOnly = On(v, "dialogOnly"), Story = On(v, "story"),
            Icon = v["icon"]?.Value<string>(), StartAfter = v["startAfter"]?.Value<string>(),
            Order = v["order"]?.Type == JTokenType.Integer || v["order"]?.Type == JTokenType.Float ? v["order"].Value<double>() : double.MaxValue
        };
        if (v["anyOf"] is JArray grp) e.AnyOfGroup = grp.Select(x => x.Value<string>()).Where(s => !string.IsNullOrEmpty(s)).ToList();   // 二选一组（09-10）
        if (v["notes"] is JObject notes) foreach (var n in notes.Properties()) e.Notes[n.Name] = n.Value.Value<string>();
        if (v["items"] is JArray items) e.Items = items.Select(x => x.Value<string>()).Where(s => !string.IsNullOrEmpty(s)).ToList();
        if (v["subs"] is JObject subs) foreach (var s in subs.Properties()) e.Subs[s.Name] = s.Value.Value<bool>();
        if (v["noCounter"] is JArray nc) foreach (var x in nc) { var s = x.Value<string>(); if (!string.IsNullOrEmpty(s)) e.NoCounter.Add(s); }
        if (v["unlockDialogue"] is JArray ud) e.UnlockDialogue = ud.Select(x => x.Value<string>()).Where(s => !string.IsNullOrEmpty(s)).ToList();
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

    // 只认字面 true：anyOf 现在可能是数组，Value<bool>() 碰上数组会抛、整条任务的 flags 作废（09-10）
    static bool On(JToken t, string name) => t[name]?.Type == JTokenType.Boolean && t[name].Value<bool>();
}
