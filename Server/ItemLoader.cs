using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Loaders;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Bundles;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Modding.Custom;
using SPTarkov.Server.Core.Services.Server;
using SPTarkov.Server.Core.Utils;
using VisitAPI.Packs;
using Path = System.IO.Path;

namespace VisitAPI.Server;

[Injectable(typePriority: OnLoadOrder.TraderRegistration)]
public class ItemLoader(CustomItemService items, TemplateTable templates, LocationTable locations, BundleLoader bundles, BundleHashCacheService bundleHashes,
    JsonUtil json, ISptLogger<ItemLoader> log) : IOnLoad
{
    readonly Dictionary<string, string> _owner = new();   // 物品 id → 包：两个包都带同一件时点名（别的模组已有的照旧沿用）

    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        // 内容包（09-23）：每个包各自的 items / loot / bundles.json + bundles\，模型包按包文件夹登记
        var bundlesAdded = 0;
        foreach (var p in ContentPacks.All(log))
        {
            LoadItems(p);
            LoadLoot(p);
            bundlesAdded += await LoadBundles(p, cancellationToken);
        }
        // 1.3.4 B8：原生 BundleLoader.LoadBundlesAsync 最后一步是 WriteCacheAsync 把这一轮见过的哈希写进 user\cache\bundleHashCache.json。
        // 它在所有 IOnLoad 之前跑完，写盘时还没有我们的包，所以包里的模型包每次开服都重算 CRC（约 63 MB）。
        // 这里照原生补上同一步：缓存服务的「本轮已见」表里此时是原生各模组的 + 我们的，整份写回，下次开服原生加载时一起读进来
        if (bundlesAdded > 0)
        {
            try { await bundleHashes.WriteCacheAsync(cancellationToken); }
            catch (Exception e) { log.Warning($"[VisitAPI] could not write the bundle hash cache (hashes will be recalculated next start, nothing else affected): {e.Message}"); }
        }
    }

    void LoadItems(PackInfo p)
    {
        foreach (var file in PackLayout.DataFiles(p, "items"))
        {
            var label = p.Name + "/" + Path.GetFileName(file);
            JsonDocument doc;
            try { doc = JsonDocument.Parse(File.ReadAllBytes(file), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }); }
            catch (Exception e) { log.Error($"[VisitAPI] items {label}: could not be read, skipping the whole file: {e.Message}"); continue; }
            using (doc)
                foreach (var entry in doc.RootElement.EnumerateObject())
                {
                    try { RecordPersonal(entry.Name, entry.Value); RecordQuestItem(entry.Name, entry.Value); }
                    catch (Exception e) { log.Warning($"[VisitAPI] items {label} {entry.Name}: could not read its 1.1 visibility rule (the item itself still loads; if it starts a story, it stays hidden in raids): {e.Message}"); }
                    try
                    {
                        if (templates.Items.ContainsKey(entry.Name))
                        {
                            if (_owner.TryGetValue(entry.Name, out var first)) log.Error($"[VisitAPI] Item {entry.Name} exists in both pack {first} and {p.Name}, using the former");
                            continue;   // 别的模组已登记：沿用它的，不重复加
                        }
                        if (Register(entry.Name, entry.Value, label)) _owner[entry.Name] = p.Name;
                    }
                    catch (Exception e) { log.Error($"[VisitAPI] items {label} {entry.Name}: {e.Message}"); }
                }
        }
    }

    bool Register(string id, JsonElement entry, string fileName)
    {
        if (!entry.TryGetProperty("template", out var tplNode) || tplNode.ValueKind != JsonValueKind.Object)
        { log.Error($"[VisitAPI] items {fileName} {id}: no template"); return false; }
        var template = json.Deserialize<TemplateItem>(tplNode.GetRawText());
        if (template == null) { log.Error($"[VisitAPI] items {fileName} {id}: template deserialization failed"); return false; }
        var locales = new Dictionary<string, LocaleDetails>(StringComparer.OrdinalIgnoreCase);
        if (entry.TryGetProperty("locales", out var loc) && loc.ValueKind == JsonValueKind.Object)
            foreach (var lang in loc.EnumerateObject())
                locales[lang.Name] = new LocaleDetails
                {
                    Name = Text(lang.Value, "Name"),
                    ShortName = Text(lang.Value, "ShortName"),
                    Description = Text(lang.Value, "Description"),
                };
        string handbookParent = null;
        double? price = null;
        if (entry.TryGetProperty("handbook", out var hb) && hb.ValueKind == JsonValueKind.Object)
        {
            handbookParent = Text(hb, "parentId");
            if (hb.TryGetProperty("price", out var p) && p.ValueKind == JsonValueKind.Number) price = p.GetDouble();
        }
        var result = items.CreateItem(new NewItemDetails
        {
            NewItem = template,
            Locales = locales,
            AddToHandbook = !string.IsNullOrEmpty(handbookParent),
            HandbookParentId = handbookParent,
            HandbookPriceRoubles = price,
            AddToFleaPriceDb = false,
            AddToWeaponShelf = false,
        }, Assembly.GetExecutingAssembly());
        if (!result.Success)
        {
            log.Error($"[VisitAPI] items {fileName} {id}: {string.Join("; ", result.Errors ?? new List<string>())}");
            return false;
        }
        return true;
    }

    static string Text(JsonElement o, string key) =>
        o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static bool Bool(JsonElement o, string key) => o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>10-06：1.1 的「个人」可读物品（模板 _props.Personal = true）谁能看到。1.1 GameWorld.ManageQuestLoot 的规矩（1.1.0 反汇编核过）：
    /// 阵营允许（AllowBear / AllowUsec / AllowSavage）、等级够（RequiredLevel）、CompletableConditions 里的物品都找到过、QuestConditions 里的任务都处于指定状态。
    /// 0.16 没有这套，这些字段原样留在包里的物品模板上；登记时记下来，QuestReadyRouter 给开章物品（visitapi.startOnItems）随 flags 带下去，客户端 ItemStart.Visible 照着判断。
    /// 别的模组已经登记了同一件物品也照记（规矩是 1.1 的数据，跟谁的模板无关）</summary>
    public static readonly Dictionary<string, PersonalRule> PersonalRules = new(StringComparer.Ordinal);

    static void RecordPersonal(string id, JsonElement entry)
    {
        if (!entry.TryGetProperty("template", out var t) || t.ValueKind != JsonValueKind.Object) return;
        if (!t.TryGetProperty("_props", out var p) || p.ValueKind != JsonValueKind.Object || !Bool(p, "Personal")) return;
        var rule = new PersonalRule
        {
            Bear = Bool(p, "AllowBear"), Usec = Bool(p, "AllowUsec"), Savage = Bool(p, "AllowSavage"),
            Level = p.TryGetProperty("RequiredLevel", out var lv) && lv.ValueKind == JsonValueKind.Number ? lv.GetInt32() : 0,
        };
        if (p.TryGetProperty("QuestConditions", out var qc) && qc.ValueKind == JsonValueKind.Array)
            foreach (var c in qc.EnumerateArray())
                if (c.ValueKind == JsonValueKind.Object && Text(c, "QuestId") is { Length: > 0 } quest)
                    rule.Quests.Add(new PersonalRule.QuestReq { Id = quest, Started = Bool(c, "Started"), Completed = Bool(c, "Completed"), Failed = Bool(c, "Failed") });
        if (p.TryGetProperty("CompletableConditions", out var cc) && cc.ValueKind == JsonValueKind.Array)
            foreach (var c in cc.EnumerateArray())
                if (c.ValueKind == JsonValueKind.Object && Text(c, "CompletableItem") is { Length: > 0 } tpl)
                    rule.Items.Add(new PersonalRule.ItemReq { Tpl = tpl, Completed = Bool(c, "IsCompleted") });
        PersonalRules[id] = rule;
    }

    /// <summary>包里写着 QuestItem = true 的物品（见 PackQuestItems）</summary>
    public static readonly HashSet<string> QuestItemWanted = new(StringComparer.Ordinal);

    static void RecordQuestItem(string id, JsonElement entry)
    {
        if (entry.TryGetProperty("template", out var t) && t.ValueKind == JsonValueKind.Object
            && t.TryGetProperty("_props", out var p) && p.ValueKind == JsonValueKind.Object && Bool(p, "QuestItem"))
            QuestItemWanted.Add(id);
    }

    void LoadLoot(PackInfo p)
    {
        foreach (var file in PackLayout.DataFiles(p, "loot"))
        {
            var label = p.Name + "/" + Path.GetFileName(file);
            Dictionary<string, List<Spawnpoint>> byMap;
            try { byMap = json.Deserialize<Dictionary<string, List<Spawnpoint>>>(File.ReadAllText(file)); }
            catch (Exception e) { log.Error($"[VisitAPI] loot {label}: could not be read, skipping the whole file: {e.Message}"); continue; }
            if (byMap == null) continue;
            foreach (var (map, points) in byMap)
            {
                if (points == null || points.Count == 0) continue;
                var location = locations.GetLocation(map);
                if (location?.LooseLoot == null) { log.Error($"[VisitAPI] loot {label}: map '{map}' not found on this server, {points.Count} spawn point(s) have nowhere to go"); continue; }
                var raw = json.Serialize(points);
                location.LooseLoot.AddTransformer(loot =>
                {
                    if (loot == null) return loot;
                    var fresh = json.Deserialize<List<Spawnpoint>>(raw) ?? new List<Spawnpoint>();
                    var forced = (loot.SpawnpointsForced ?? Enumerable.Empty<Spawnpoint>()).ToList();
                    var have = forced.Select(p => p.Template?.Id).Where(x => x != null).ToHashSet(StringComparer.Ordinal);
                    forced.AddRange(fresh.Where(p => p.Template?.Id == null || !have.Contains(p.Template.Id)));
                    loot.SpawnpointsForced = forced;
                    return loot;
                });
            }
        }
    }

    async Task<int> LoadBundles(PackInfo p, CancellationToken ct)
    {
        var manifestFile = PackLayout.BundlesManifest(p);
        if (!File.Exists(manifestFile)) return 0;
        BundleManifest manifest;
        try { manifest = await json.DeserializeFromFileAsync<BundleManifest>(manifestFile, ct); }
        catch (Exception e) { log.Error($"[VisitAPI] Could not read bundles.json of {p.Name}: {e.Message}"); return 0; }
        if (manifest?.Manifest == null) return 0;
        // SPT 按 ModPath/bundles/key 找文件，所以模型文件住在包自己的 bundles\ 下、ModPath 就是包文件夹（老布局 = 模组根目录，和以前一样）
        var modPath = ContentPacks.Rel(p);
        var added = 0;
        foreach (var entry in manifest.Manifest)
        {
            if (string.IsNullOrEmpty(entry.Key)) continue;
            if (bundles.GetBundle(entry.Key) != null) continue;   // 别的模组或先加载的包已提供，沿用
            var path = Path.Combine(PackLayout.BundlesDir(p), entry.Key.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) { log.Error($"[VisitAPI] Bundle file missing: {path}"); continue; }
            var hash = await BundleHash(Path.Join(modPath, "bundles", entry.Key).Replace('\\', '/'), ct);
            if (hash == null) { log.Error($"[VisitAPI] Bundle is not a valid Unity bundle: {entry.Key}"); continue; }
            var info = new BundleInfo { ModPath = modPath, Bundle = entry, Crc = hash.Value.Crc };
            BundleSize?.SetValue(info, hash.Value.Size);
            BundleTicks?.SetValue(info, hash.Value.Ticks);
            bundles.AddBundle(entry.Key, info);
            added++;
        }
        return added;
    }

    /// 10-06（SPT 4.1.x 全兼容，见 csproj）：模型包校验值在 4.1.0 之后换了写法——4.1.0 是 CalculateMatchAndStoreHashAsync（只回 CRC），
    /// 4.1.6 是 GetOrCalculateHashAsync（回 CRC + 大小 + 修改时间，不是 Unity 包就回 null），BundleInfo 也多了必填的 Size / ModifiedUtcTicks。
    /// 现在照 4.1.0 编，这里看当前服务端有哪个方法就调哪个；新字段 4.1.0 的 BundleInfo 里没有，有就用反射填上
    static readonly MethodInfo NewHash = typeof(BundleHashCacheService).GetMethod("GetOrCalculateHashAsync", [typeof(string), typeof(CancellationToken)]);
    static readonly MethodInfo OldHash = typeof(BundleHashCacheService).GetMethod("CalculateMatchAndStoreHashAsync", [typeof(string), typeof(CancellationToken)]);
    static readonly PropertyInfo BundleSize = typeof(BundleInfo).GetProperty("Size");
    static readonly PropertyInfo BundleTicks = typeof(BundleInfo).GetProperty("ModifiedUtcTicks");

    async Task<(uint Crc, long Size, long Ticks)?> BundleHash(string path, CancellationToken ct)
    {
        if (NewHash != null)
        {
            var task = (Task)NewHash.Invoke(bundleHashes, [path, ct]);
            await task;
            var entry = task.GetType().GetProperty("Result")?.GetValue(task);
            if (entry == null) return null;
            var t = entry.GetType();
            return (Convert.ToUInt32(t.GetProperty("Crc")?.GetValue(entry)), Convert.ToInt64(t.GetProperty("Size")?.GetValue(entry)), Convert.ToInt64(t.GetProperty("ModifiedUtcTicks")?.GetValue(entry)));
        }
        if (OldHash != null)
        {
            var crc = await (Task<uint>)OldHash.Invoke(bundleHashes, [path, ct]);
            var file = new FileInfo(path);
            return (crc, file.Length, file.LastWriteTimeUtc.Ticks);
        }
        if (!_noHashLogged) log.Error("[VisitAPI] This SPT version has no known bundle hash method (neither GetOrCalculateHashAsync nor CalculateMatchAndStoreHashAsync); pack item models will not load");
        _noHashLogged = true;
        return null;
    }

    static bool _noHashLogged;
}

/// <summary>10-06 SORA 实测开章线索「还是进背包里了啊」：Manimal 的立交桥 backport 自己也登记了 EMERCOM 传单（照 1.1 数据 QuestItem = false），
/// 同一件物品先登记的赢，包里改成任务物品的那份没生效（包里 75 件物品逐件比过别的模组，只有这一处不一致）。
/// 这里等所有模组都加载完，把包里写着 QuestItem = true、却被别人登记成 false 的物品改回任务物品——只动这一个字段，模板其余部分仍用对方的。
/// /client/items 是请求时现读 templates.Items，所以在这里改就能下发到客户端</summary>
[Injectable(typePriority: OnLoadOrder.PostLoad + 1)]
public class PackQuestItems(TemplateTable templates) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        foreach (var id in ItemLoader.QuestItemWanted)
            if (templates.Items.TryGetValue(id, out var t) && t?.Properties != null && t.Properties.QuestItem != true)
                t.Properties.QuestItem = true;
        return Task.CompletedTask;
    }
}

/// <summary>1.1 CompletableTemplate 的可见条件，见 ItemLoader.PersonalRules</summary>
public sealed class PersonalRule
{
    public sealed class QuestReq { public string Id; public bool Started, Completed, Failed; }
    public sealed class ItemReq { public string Tpl; public bool Completed; }
    public readonly List<QuestReq> Quests = new();
    public readonly List<ItemReq> Items = new();
    public bool Bear, Usec, Savage;
    public int Level;
}
