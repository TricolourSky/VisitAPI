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
            var hash = await bundleHashes.GetOrCalculateHashAsync(Path.Join(modPath, "bundles", entry.Key).Replace('\\', '/'), ct);
            if (hash == null) { log.Error($"[VisitAPI] Bundle is not a valid Unity bundle: {entry.Key}"); continue; }
            bundles.AddBundle(entry.Key, new BundleInfo { ModPath = modPath, Bundle = entry, Crc = hash.Crc, Size = hash.Size, ModifiedUtcTicks = hash.ModifiedUtcTicks });
            added++;
        }
        return added;
    }
}
