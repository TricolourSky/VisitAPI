using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Services.Modding.Custom;
using SPTarkov.Server.Core.Utils;
using VisitAPI.Packs;
using Path = System.IO.Path;

namespace VisitAPI.Server;

[Injectable(typePriority: OnLoadOrder.PostLoad)]
public class QuestLoader(CustomQuestService questService, ImageRouter images, JsonUtil json, LocaleTable localeTable, TemplateTable templates, ISptLogger<QuestLoader> log) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        // 内容包（09-23）：每个包各自的 images / locales / quests，合并时撞车点名、先来的赢（ContentPacks / PackLayout）
        var packs = ContentPacks.All(log);
        var routes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in packs) RegisterImages(p, routes);
        var locales = MergeLocales(packs);
        foreach (var lang in localeTable.Global.Keys)
            if (!locales.ContainsKey(lang)) locales[lang] = new Dictionary<string, string>();
        var all = new Dictionary<string, Quest>();
        var owner = new Dictionary<string, string>();   // 任务 id → 包：两个包都有同一条任务时点名，用先来的
        Dictionary<string, Dictionary<string, string>> brief = null;   // 交给 CreateQuest 的精简文案，见 AddQuestLocales
        foreach (var p in packs)
        {
            foreach (var file in PackLayout.DataFiles(p, "quests"))
            {
                var parsed = Parse<Dictionary<string, Quest>>(file);
                if (parsed == null) continue;
                foreach (var quest in parsed.Values)
                {
                    var id = quest.Id.ToString();
                    if (owner.TryGetValue(id, out var first)) { log.Error($"[VisitAPI] Quest {id} exists in both pack {first} and {p.Name} ({Path.GetFileName(file)}), using the one from {first}"); continue; }
                    owner[id] = p.Name;
                    Loaded.Add(id);
                    NativeUnlockReward(quest, Path.GetFileName(file));
                    FixZoneIds(quest, Path.GetFileName(file));
                    BlankMailTexts(quest, locales);
                    if (brief == null && !templates.Quests.ContainsKey(quest.Id)) brief = AddQuestLocales(locales);
                    var result = questService.CreateQuest(new NewQuestDetails { NewQuest = quest, Locales = brief ?? locales });
                    if (result.Success) all[id] = quest;
                    else log.Error($"[VisitAPI] quest {id} ({p.Name}/{Path.GetFileName(file)}): {string.Join("; ", result.Errors ?? new List<string>())}");
                }
            }
        }
        NameStoryQuests(all);
        WarnDangling(all);
        return Task.CompletedTask;
    }

    /// <summary>10-17 审查 E25：合并文案表每种语言只挂一次。每条任务都把整张表交给 CreateQuest 的话，SPT 每种语言挂一个遍历整张表的变换器，
    /// 160 条任务 = 160 个一样的，LazyLoad.Value 每取一次全部重跑，真正干活的只有第一个。挂的时刻：第一条真正进任务表的任务之前
    ///（CreateQuest 撞了已有 id 时不挂文案）；空表、语言表里没有的语言照 SPT 的 AddQuestLocales 不挂。
    /// 返回以后交给 CreateQuest 的文案：语言一个不少，每种只一条 RewardMailKey（BlankMailTexts 补过），SPT 照样逐语言校验、报错</summary>
    Dictionary<string, Dictionary<string, string>> AddQuestLocales(Dictionary<string, Dictionary<string, string>> locales)
    {
        foreach (var (lang, table) in locales)
            if (table.Count > 0 && localeTable.Global.TryGetValue(lang, out var lazy)) LocaleText.Add(lazy, table);
        return locales.ToDictionary(kv => kv.Key, kv => new Dictionary<string, string> { [RewardMailKey] = kv.Value[RewardMailKey] });
    }

    /// <summary>各包的 locales\&lt;语言&gt;.json 按语言合并：同键同文没事；同键不同文点名（每包最多列 5 处）、先来的赢。</summary>
    Dictionary<string, Dictionary<string, string>> MergeLocales(IReadOnlyList<PackInfo> packs)
    {
        var locales = new Dictionary<string, Dictionary<string, string>>();
        var owner = new Dictionary<string, string>();
        foreach (var p in packs)
            foreach (var f in ContentPacks.Locales(p, json))
            {
                if (f.Error != null) { log.Error($"[VisitAPI] cannot parse {Path.GetFileName(f.File)}: {f.Error}"); continue; }
                var lang = Path.GetFileNameWithoutExtension(f.File);
                // 文件名不是 SPT 认的语言（zh.json 之类）：进了合并表会让 CreateQuest 对每条任务报 could_not_find_language_key、整包任务收不到文案（10-29）
                if (!localeTable.Global.ContainsKey(lang)) { log.Warning($"[VisitAPI] {p.Name}/locales/{Path.GetFileName(f.File)}: '{lang}' is not a language SPT knows ({string.Join(", ", localeTable.Global.Keys)}), skipped"); continue; }
                var table = f.Table;
                if (table == null) continue;
                if (!locales.TryGetValue(lang, out var merged)) locales[lang] = merged = new Dictionary<string, string>();
                var clash = 0;
                foreach (var (k, v) in table)
                {
                    if (merged.TryAdd(k, v)) { owner[lang + " " + k] = p.Name; continue; }
                    if (merged[k] != v && clash++ < 5) log.Error($"[VisitAPI] Locale {lang} '{k}' differs between pack {owner[lang + " " + k]} and {p.Name}, using the former");
                }
                if (clash > 5) log.Error($"[VisitAPI] Locale {lang}: pack {p.Name} has {clash - 5} more key(s) with conflicting text, not listed individually");
            }
        return locales;
    }

    internal const string RewardMailKey = "68fa00bda5ef093c440fb0ba 0";

    static readonly Dictionary<string, string> RewardMailText = new()
    {
        ["ch"] = "现在归你了。",
        ["cz"] = "Teď je to tvoje.",
        ["en"] = "It's yours now.",
        ["es"] = "Ahora es tuyo.",
        ["es-mx"] = "Ahora es tuyo.",
        ["fr"] = "C'est à vous maintenant.",
        ["it"] = "Adesso è tua.",
        ["kr"] = "이제 네 거다.",
        ["pl"] = "To teraz twoje.",
        ["po"] = "Agora é seu.",
        ["ru"] = "Это теперь твоё.",
    };

    public static readonly HashSet<string> Loaded = new();

    static void BlankMailTexts(Quest quest, Dictionary<string, Dictionary<string, string>> locales)
    {
        var own = locales.Values.Any(t => t.TryGetValue(quest.Id + " successMessageText", out var s) && !string.IsNullOrWhiteSpace(s));
        if (!own)
        {
            quest.SuccessMessageText = RewardMailKey;
            quest.StartedMessageText = RewardMailKey;
        }
        foreach (var (lang, table) in locales)
        {
            var text = RewardMailText.TryGetValue(lang, out var t) ? t : RewardMailText["en"];
            table.TryAdd(RewardMailKey, text);
            if (!own)
            {
                table.TryAdd(quest.Id + " successMessageText", text);
                table.TryAdd(quest.Id + " startedMessageText", text);
            }
            foreach (var suffix in new[] { "description", "failMessageText" })
                table.TryAdd(quest.Id + " " + suffix, "");
        }
    }

    void NativeUnlockReward(Quest quest, string fileName)
    {
        try
        {
            var vx = Ext.Visit(quest?.ExtensionData);
            if (!Ext.Flag(vx, "unlockTraderOnReady")) return;
            quest.Rewards ??= new Dictionary<string, List<Reward>>();
            if (!quest.Rewards.TryGetValue("Success", out var success) || success == null) quest.Rewards["Success"] = success = new List<Reward>();
            if (success.Any(r => r?.Type == RewardType.TraderUnlock)) return;
            var target = Ext.Str(vx, "unlockTrader") is { Length: 24 } named ? named : quest.TraderId.ToString();
            if (target?.Length != 24) return;
            success.Add(new Reward { Id = new MongoId(), Type = RewardType.TraderUnlock, Target = target, Index = success.Count, Unknown = false });
        }
        catch (Exception e) { log.Error($"[VisitAPI] quest {quest?.Id} ({fileName}): could not add native TraderUnlock reward: {e.Message}"); }
    }

    void FixZoneIds(Quest quest, string fileName)
    {
        try
        {
            foreach (var list in new[] { quest.Conditions?.AvailableForFinish, quest.Conditions?.AvailableForStart, quest.Conditions?.Fail })
                foreach (var c in list ?? new List<QuestCondition>())
                {
                    if (c.ConditionType is not ("LeaveItemAtLocation" or "PlaceBeacon") || !string.IsNullOrEmpty(c.ZoneId)) continue;
                    if (Ext.Get(c.ExtensionData, "zoneIds", JsonValueKind.Array) is not { } ze) continue;
                    foreach (var e in ze.EnumerateArray())
                        if (e.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(e.GetString())) { c.ZoneId = e.GetString(); break; }
                }
        }
        catch (Exception e) { log.Error($"[VisitAPI] quest {quest?.Id} ({fileName}): zoneIds fix-up failed: {e.Message}"); }
    }

    /// 任务条件 / visitapi.startAfter 里写错的任务 id（不是 24 位十六进制）报 Error；引用了还没移植的任务是已知的数据缺口，不报（09-28 SORA：日志只在出问题时打）
    void WarnDangling(Dictionary<string, Quest> all)
    {
        var known = new HashSet<string>(all.Keys);
        foreach (var (id, q) in all)
        {
            var targets = new List<(string what, string target)>();
            foreach (var (status, list) in new[] { ("AvailableForStart", q.Conditions?.AvailableForStart), ("AvailableForFinish", q.Conditions?.AvailableForFinish), ("Fail", q.Conditions?.Fail) })
                foreach (var c in list ?? new List<QuestCondition>())
                    foreach (var t in Ext.QuestCondTargets(c)) targets.Add((status, t));
            foreach (var s in StartAfterOf(q)) targets.Add(("visitapi.startAfter", s));
            foreach (var (what, target) in targets)
                if (!known.Contains(target) && !MongoId.IsValidMongoId(target))
                    log.Error($"[VisitAPI] quest {id} {what} says '{target}', which is not a valid 24-char hex quest id (most likely a typo or extra space from hand-editing the JSON); this quest can never reach that step");
        }
    }

    void NameStoryQuests(Dictionary<string, Quest> all)
    {
        var chapterOf = new Dictionary<string, string>();
        var chapters = all.Where(kv => Ext.Flag(Ext.Visit(kv.Value.ExtensionData), "chapter")).Select(kv => kv.Key).ToList();
        foreach (var id in chapters)
            foreach (var sub in QuestTargets(all[id], all))
                if (sub != id && !chapterOf.ContainsKey(sub)) chapterOf[sub] = id;
        // 10-06 意外证人：章节 visitapi.startAfter 点名的起点也算这一章的（隐藏的开章任务 68f0f549 不在章节的完成条件里，上面找不到它）——
        // 不然它没名字，Dynamic Maps 给它画的目标点直接显示「<id> name」原始键（SORA 实测「任务名称是乱码」）
        foreach (var id in chapters)
            foreach (var s in StartAfterOf(all[id]))
                if (s != id && all.ContainsKey(s) && !chapters.Contains(s) && !chapterOf.ContainsKey(s)) chapterOf[s] = id;
        foreach (var id in chapters)
        {
            var queue = new Queue<string>(chapterOf.Where(kv => kv.Value == id).Select(kv => kv.Key).ToList());
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (!all.TryGetValue(cur, out var sq)) continue;
                foreach (var t in QuestTargets(sq, all))
                {
                    if (t == id || chapterOf.ContainsKey(t) || chapters.Contains(t)) continue;
                    chapterOf[t] = id;
                    queue.Enqueue(t);
                }
            }
        }
        StoryQuestMail.RewardsOnly = RewardsOnly(all, chapters, chapterOf);
        if (chapterOf.Count == 0) return;
        foreach (var (_, lazy) in localeTable.Global)
            lazy.AddTransformer(data =>
            {
                if (data == null) return data;
                foreach (var (sub, chapter) in chapterOf)
                {
                    var key = sub + " name";
                    if (data.TryGetValue(key, out var existing) && !string.IsNullOrWhiteSpace(existing)) continue;
                    if (data.TryGetValue(chapter + " name", out var name) && !string.IsNullOrWhiteSpace(name)) data[key] = name;
                }
                return data;
            });
    }

    /// 10-01：章节 visitapi.mailRewardsOnly=true → 章节 + 名下全部子任务进「没附件不寄信」名单（StoryQuestMail）
    static HashSet<string> RewardsOnly(Dictionary<string, Quest> all, List<string> chapters, Dictionary<string, string> chapterOf)
    {
        var rewardsOnly = new HashSet<string>();
        foreach (var id in chapters)
        {
            if (!Ext.Flag(Ext.Visit(all[id].ExtensionData), "mailRewardsOnly")) continue;
            rewardsOnly.Add(id);
            foreach (var (sub, chapter) in chapterOf) if (chapter == id) rewardsOnly.Add(sub);
        }
        return rewardsOnly;
    }

    /// visitapi.startAfter：一个任务 id（字符串）或多个（数组），空的跳过
    static IEnumerable<string> StartAfterOf(Quest q)
    {
        if (Ext.Visit(q.ExtensionData) is not { } vx || !vx.TryGetProperty("startAfter", out var sa)) yield break;
        if (sa.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(sa.GetString())) yield return sa.GetString()!;
        else if (sa.ValueKind == JsonValueKind.Array)
            foreach (var e in sa.EnumerateArray()) if (e.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(e.GetString())) yield return e.GetString()!;
    }

    static IEnumerable<string> QuestTargets(Quest q, Dictionary<string, Quest> all)
    {
        foreach (var bucket in new[] { q.Conditions?.AvailableForFinish, q.Conditions?.Fail })
            foreach (var c in bucket ?? new List<QuestCondition>())
                foreach (var t in Ext.QuestCondTargets(c))
                    if (all.ContainsKey(t)) yield return t;
    }

    /// <summary>包内 images\banners → /files/quest/icon/…、images\icons → /files/quest/chapters_icon/…（1.1 的地址不变，见 PackLayout.ImageFolders）。
    /// 路由是全局的：两个包同名的图撞了点名、先来的赢（SPT 自己是后登记的盖前面的，所以这里不重复登记）。</summary>
    void RegisterImages(PackInfo p, Dictionary<string, string> routes)
    {
        foreach (var (route, dir) in PackLayout.ImageFolders(p))
            foreach (var file in Directory.GetFiles(dir))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (name.Length == 0) continue;
                if (name.Contains('.'))
                {
                    log.Warning($"[VisitAPI] Quest image {p.Name}/{route}/{Path.GetFileName(file)} has a dot in its file name, SPT would truncate it; skipped");
                    continue;
                }
                var key = $"/files/quest/{route}/{name}";
                if (routes.TryGetValue(key, out var first)) { log.Error($"[VisitAPI] Image {route}/{name} exists in both pack {first} and {p.Name}, using the former"); continue; }
                routes[key] = p.Name;
                images.AddRoute(key, file);
            }
    }

    T Parse<T>(string file) where T : class
    {
        try { return json.DeserializeFromFile<T>(file); }
        catch (Exception e) { log.Error($"[VisitAPI] cannot parse {Path.GetFileName(file)}: {e.Message}"); return null; }
    }
}
