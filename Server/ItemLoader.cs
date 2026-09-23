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
        foreach (var p in ContentPacks.All(log))
        {
            LoadItems(p);
            LoadLoot(p);
            await LoadBundles(p, cancellationToken);
        }
    }

    void LoadItems(PackInfo p)
    {
        int ok = 0, skipped = 0;
        foreach (var file in PackLayout.DataFiles(p, "items"))
        {
            var label = p.Name + "/" + Path.GetFileName(file);
            JsonDocument doc;
            try { doc = JsonDocument.Parse(File.ReadAllBytes(file), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }); }
            catch (Exception e) { log.Error($"[VisitAPI] items {label}: 读不动，整个跳过: {e.Message}"); continue; }
            using (doc)
                foreach (var entry in doc.RootElement.EnumerateObject())
                {
                    try
                    {
                        if (templates.Items.ContainsKey(entry.Name))
                        {
                            if (_owner.TryGetValue(entry.Name, out var first)) log.Error($"[VisitAPI] 物品 {entry.Name} 在包 {first} 和 {p.Name} 里都有，用了前者的");
                            skipped++;   // 别的模组已登记：沿用它的，不重复加
                            continue;
                        }
                        if (Register(entry.Name, entry.Value, label)) { ok++; _owner[entry.Name] = p.Name; }
                    }
                    catch (Exception e) { log.Error($"[VisitAPI] items {label} {entry.Name}: {e.Message}"); }
                }
        }
        if (ok + skipped > 0) log.Info($"[VisitAPI] 包 {p.Label}：物品 {ok} 件登记，{skipped} 件别处已有");
    }

    bool Register(string id, JsonElement entry, string fileName)
    {
        if (!entry.TryGetProperty("template", out var tplNode) || tplNode.ValueKind != JsonValueKind.Object)
        { log.Error($"[VisitAPI] items {fileName} {id}: 没有 template"); return false; }
        var template = json.Deserialize<TemplateItem>(tplNode.GetRawText());
        if (template == null) { log.Error($"[VisitAPI] items {fileName} {id}: template 反序列化失败"); return false; }
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
            catch (Exception e) { log.Error($"[VisitAPI] loot {label}: 读不动，整个跳过: {e.Message}"); continue; }
            if (byMap == null) continue;
            foreach (var (map, points) in byMap)
            {
                if (points == null || points.Count == 0) continue;
                var location = locations.GetLocation(map);
                if (location?.LooseLoot == null) { log.Error($"[VisitAPI] loot {label}: 本机没有地图 '{map}'，{points.Count} 个刷新点没处放"); continue; }
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

    async Task LoadBundles(PackInfo p, CancellationToken ct)
    {
        var manifestFile = PackLayout.BundlesManifest(p);
        if (!File.Exists(manifestFile)) return;
        BundleManifest manifest;
        try { manifest = await json.DeserializeFromFileAsync<BundleManifest>(manifestFile, ct); }
        catch (Exception e) { log.Error($"[VisitAPI] {p.Name} 的 bundles.json 读不动: {e.Message}"); return; }
        if (manifest?.Manifest == null) return;
        // SPT 按 ModPath/bundles/key 找文件，所以模型文件住在包自己的 bundles\ 下、ModPath 就是包文件夹（老布局 = 模组根目录，和以前一样）
        var modPath = ContentPacks.Rel(p);
        int added = 0, reused = 0;
        foreach (var entry in manifest.Manifest)
        {
            if (string.IsNullOrEmpty(entry.Key)) continue;
            if (bundles.GetBundle(entry.Key) != null) { reused++; continue; }   // 别的模组或先加载的包已提供，沿用
            var path = Path.Combine(PackLayout.BundlesDir(p), entry.Key.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) { log.Error($"[VisitAPI] 模型包文件不在: {path}"); continue; }
            var hash = await bundleHashes.GetOrCalculateHashAsync(Path.Join(modPath, "bundles", entry.Key).Replace('\\', '/'), ct);
            if (hash == null) { log.Error($"[VisitAPI] 模型包不是合法的 Unity 包: {entry.Key}"); continue; }
            bundles.AddBundle(entry.Key, new BundleInfo { ModPath = modPath, Bundle = entry, Crc = hash.Crc, Size = hash.Size, ModifiedUtcTicks = hash.ModifiedUtcTicks });
            added++;
        }
        log.Info($"[VisitAPI] 包 {p.Label}：模型包 {added} 个登记，{reused} 个别处已有");
    }
}
