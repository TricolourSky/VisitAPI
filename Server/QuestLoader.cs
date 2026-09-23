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
        var ok = 0;
        var blanked = 0;
        var all = new Dictionary<string, Quest>();
        var owner = new Dictionary<string, string>();   // 任务 id → 包：两个包都有同一条任务时点名，用先来的
        foreach (var p in packs)
        {
            var n = 0;
            foreach (var file in PackLayout.DataFiles(p, "quests"))
            {
                var parsed = Parse<Dictionary<string, Quest>>(file);
                if (parsed == null) continue;
                foreach (var quest in parsed.Values)
                {
                    var id = quest.Id.ToString();
                    if (owner.TryGetValue(id, out var first)) { log.Error($"[VisitAPI] 任务 {id} 在包 {first} 和 {p.Name}（{Path.GetFileName(file)}）里都有，用了 {first} 的"); continue; }
                    owner[id] = p.Name;
                    NativeUnlockReward(quest, Path.GetFileName(file));
                    FixZoneIds(quest, Path.GetFileName(file));
                    blanked += BlankMailTexts(quest, locales);
                    var result = questService.CreateQuest(new NewQuestDetails { NewQuest = quest, Locales = locales });
                    if (result.Success) { ok++; n++; all[id] = quest; }
                    else log.Error($"[VisitAPI] quest {id} ({p.Name}/{Path.GetFileName(file)}): {string.Join("; ", result.Errors ?? new List<string>())}");
                }
            }
            log.Info($"[VisitAPI] 包 {p.Label}：任务 {n} 条");
        }
        NameStoryQuests(all);
        WarnDangling(all);
        return Task.CompletedTask;
    }

    /// <summary>各包的 locales\&lt;语言&gt;.json 按语言合并：同键同文没事；同键不同文点名（每包最多列 5 处）、先来的赢。</summary>
    Dictionary<string, Dictionary<string, string>> MergeLocales(IReadOnlyList<PackInfo> packs)
    {
        var locales = new Dictionary<string, Dictionary<string, string>>();
        var owner = new Dictionary<string, string>();
        foreach (var p in packs)
            foreach (var file in PackLayout.DataFiles(p, "locales"))
            {
                var lang = Path.GetFileNameWithoutExtension(file);
                var table = Parse<Dictionary<string, string>>(file);
                if (table == null) continue;
                if (!locales.TryGetValue(lang, out var merged)) locales[lang] = merged = new Dictionary<string, string>();
                var clash = 0;
                foreach (var (k, v) in table)
                {
                    if (merged.TryAdd(k, v)) { owner[lang + " " + k] = p.Name; continue; }
                    if (merged[k] != v && clash++ < 5) log.Error($"[VisitAPI] 文案 {lang}「{k}」在包 {owner[lang + " " + k]} 和 {p.Name} 里不一样，用了前者的");
                }
                if (clash > 5) log.Error($"[VisitAPI] 文案 {lang}：包 {p.Name} 还有 {clash - 5} 处同键不同文，没逐条列");
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

    static int BlankMailTexts(Quest quest, Dictionary<string, Dictionary<string, string>> locales)
    {
        var added = 0;
        Loaded.Add(quest.Id.ToString());
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
                if (table.TryAdd(quest.Id + " successMessageText", text)) added++;
                if (table.TryAdd(quest.Id + " startedMessageText", text)) added++;
            }
            foreach (var suffix in new[] { "description", "failMessageText" })
                if (table.TryAdd(quest.Id + " " + suffix, "")) added++;
        }
        return added;
    }

    void NativeUnlockReward(Quest quest, string fileName)
    {
        try
        {
            if (quest?.ExtensionData == null || !quest.ExtensionData.TryGetValue("visitapi", out var v) || v is not JsonElement vx || vx.ValueKind != JsonValueKind.Object) return;
            if (!vx.TryGetProperty("unlockTraderOnReady", out var flag) || flag.ValueKind != JsonValueKind.True) return;
            quest.Rewards ??= new Dictionary<string, List<Reward>>();
            if (!quest.Rewards.TryGetValue("Success", out var success) || success == null) quest.Rewards["Success"] = success = new List<Reward>();
            if (success.Any(r => r?.Type == RewardType.TraderUnlock)) return;
            var target = vx.TryGetProperty("unlockTrader", out var t) && t.ValueKind == JsonValueKind.String && t.GetString()?.Length == 24 ? t.GetString() : quest.TraderId.ToString();
            if (string.IsNullOrEmpty(target) || target.Length != 24) return;
            success.Add(new Reward { Id = new MongoId(), Type = RewardType.TraderUnlock, Target = target, Index = success.Count, Unknown = false });
        }
        catch (System.Exception e) { log.Error($"[VisitAPI] quest {quest?.Id} ({fileName}): could not add native TraderUnlock reward: {e.Message}"); }
    }

    void FixZoneIds(Quest quest, string fileName)
    {
        try
        {
            var n = 0;
            foreach (var list in new[] { quest.Conditions?.AvailableForFinish, quest.Conditions?.AvailableForStart, quest.Conditions?.Fail })
                foreach (var c in list ?? new List<QuestCondition>())
                {
                    if (c.ConditionType is not ("LeaveItemAtLocation" or "PlaceBeacon") || !string.IsNullOrEmpty(c.ZoneId)) continue;
                    if (c.ExtensionData == null || !c.ExtensionData.TryGetValue("zoneIds", out var z) || z is not JsonElement ze || ze.ValueKind != JsonValueKind.Array) continue;
                    foreach (var e in ze.EnumerateArray())
                        if (e.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(e.GetString())) { c.ZoneId = e.GetString(); n++; break; }
                }
        }
        catch (System.Exception e) { log.Error($"[VisitAPI] quest {quest?.Id} ({fileName}): zoneIds fix-up failed: {e.Message}"); }
    }

    void WarnDangling(Dictionary<string, Quest> all)
    {
        var known = new HashSet<string>(all.Keys);
        foreach (var (id, q) in all)
        {
            var targets = new List<(string what, string target)>();
            foreach (var (status, list) in new[] { ("AvailableForStart", q.Conditions?.AvailableForStart), ("AvailableForFinish", q.Conditions?.AvailableForFinish), ("Fail", q.Conditions?.Fail) })
                foreach (var c in list ?? new List<QuestCondition>())
                {
                    if (c.ConditionType != "Quest" || c.Target == null) continue;
                    foreach (var t in c.Target.IsItem ? new List<string> { c.Target.Item } : c.Target.List ?? new List<string>())
                        if (!string.IsNullOrEmpty(t)) targets.Add((status, t));
                }
            if (q.ExtensionData != null && q.ExtensionData.TryGetValue("visitapi", out var v) && v is JsonElement vx && vx.ValueKind == JsonValueKind.Object
                && vx.TryGetProperty("startAfter", out var sa) && sa.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(sa.GetString()))
                targets.Add(("visitapi.startAfter", sa.GetString()!));
            foreach (var (what, target) in targets)
            {
                if (known.Contains(target)) continue;
                if (!MongoId.IsValidMongoId(target))
                {
                    log.Error($"[VisitAPI] quest {id} 的 {what} 写的是「{target}」，不是合法的 24 位十六进制任务 id（多半是手改 JSON 时敲错或多了空格），这条任务永远到不了那一步");
                    continue;
                }
                if (!templates.Quests.ContainsKey(new MongoId(target)))
                    log.Error($"[VisitAPI] quest {id} 的 {what} 指向本机没有的任务 {target}——对应的任务文件没装（塔科夫之旅和陨落星辰要一起装），这条任务永远到不了那一步");
            }
        }
    }

    void NameStoryQuests(Dictionary<string, Quest> all)
    {
        var chapterOf = new Dictionary<string, string>();
        var chapters = all.Where(kv => kv.Value.ExtensionData != null && kv.Value.ExtensionData.TryGetValue("visitapi", out var v) && v is JsonElement vx
                                       && vx.ValueKind == JsonValueKind.Object && vx.TryGetProperty("chapter", out var ch) && ch.ValueKind == JsonValueKind.True)
                          .Select(kv => kv.Key).ToList();
        foreach (var id in chapters)
            foreach (var sub in QuestTargets(all[id], all))
                if (sub != id && !chapterOf.ContainsKey(sub)) chapterOf[sub] = id;
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

    static IEnumerable<string> QuestTargets(Quest q, Dictionary<string, Quest> all)
    {
        var buckets = new[] { q.Conditions?.AvailableForFinish, q.Conditions?.Fail };
        foreach (var bucket in buckets)
            foreach (var c in bucket ?? new List<QuestCondition>())
            {
                if (c.ConditionType != "Quest" || c.Target == null) continue;
                var targets = c.Target.IsItem ? new List<string> { c.Target.Item } : c.Target.List ?? new List<string>();
                foreach (var t in targets) if (!string.IsNullOrEmpty(t) && all.ContainsKey(t)) yield return t;
            }
    }

    /// <summary>包内 images\banners → /files/quest/icon/…、images\icons → /files/quest/chapters_icon/…（1.1 的地址不变，见 PackLayout.ImageFolders）。
    /// 路由是全局的：两个包同名的图撞了点名、先来的赢（SPT 自己是后登记的盖前面的，所以这里不重复登记）。</summary>
    void RegisterImages(PackInfo p, Dictionary<string, string> routes)
    {
        var n = 0;
        foreach (var (route, dir) in PackLayout.ImageFolders(p))
            foreach (var file in Directory.GetFiles(dir))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (name.Length == 0) continue;
                if (name.Contains('.'))
                {
                    log.Warning($"[VisitAPI] 任务图 {p.Name}/{route}/{Path.GetFileName(file)} 的文件名里有点号，SPT 会把它截断，已跳过");
                    continue;
                }
                var key = $"/files/quest/{route}/{name}";
                if (routes.TryGetValue(key, out var first)) { log.Error($"[VisitAPI] 图片 {route}/{name} 在包 {first} 和 {p.Name} 里都有，用了前者的"); continue; }
                routes[key] = p.Name;
                images.AddRoute(key, file);
                n++;
            }
        if (n > 0) log.Info($"[VisitAPI] 包 {p.Label}：任务图 {n} 张");
    }

    T Parse<T>(string file) where T : class
    {
        try { return json.DeserializeFromFile<T>(file); }
        catch (System.Exception e) { log.Error($"[VisitAPI] cannot parse {Path.GetFileName(file)}: {e.Message}"); return null; }
    }
}
