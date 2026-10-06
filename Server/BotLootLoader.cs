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
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Generators.Loot;
using SPTarkov.Server.Core.Helpers.Bot;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Bots;
using SPTarkov.Server.Core.Models.Spt.Tables;
using VisitAPI.Packs;
using Path = System.IO.Path;

namespace VisitAPI.Server;

/// <summary>10-06 意外证人：剧情要「找到并消灭 Reshala」「他肯定随身带着钥匙」。1.1（SPT5 数据）里这把储藏处钥匙只在 Reshala 口袋池里占 16 / 2400，
/// 几乎掉不出来；SORA 定「Reshala 身上带」。内容包 botloot\*.json 按机器人类型写：
///   "ensure": [物品 id…]           —— 必带：机器人生成完（SPT 4.1.6 BotLootGenerator.GenerateLoot 之后）身上没有就补一件；
///   "specialLoot" / "specialItems" —— 改专属物品池 / 件数（SpecialLoot 池 + generation.specialItems 权重，按 SPT 原生规则抽）。
/// 第一版只用了专属物品池，SORA 实测「Reshala 身上没看见」：他没胸挂没背包，武器备用弹匣先塞满口袋时钥匙放不进去（试生成 5 个只有 3 个带）。
/// 「必带」先照原生的放法试（口袋 / 胸挂 / 背包有空就放）；放不下就把口袋里的一件（优先备用弹匣）原地换成它——同格同位置，
/// SPT 的容器占位缓存（BotInventoryContainerService）不用动。
/// 同日 SORA 又定「reshala身上的钥匙改为和正式版一样概率刷新」：包里改用 "pockets"（往口袋物品池加权重，原生照抽），不再写 ensure。
/// SPT5 的 Reshala 口袋池 33 件共 2400，钥匙 16（每抽一件 1/150，按口袋件数权重算一个 Reshala 约 1%）；SPT 4.1 的池子 209 件共 143300，
/// 同比例换算成 962，概率与正式版一致。格式：{ "&lt;bot 类型，小写&gt;": { "pockets" / "backpack" / "vest": {物品: 权重}, "ensure": [...], "specialLoot": {...}, "specialItems": {...} } }</summary>
[Injectable(typePriority: OnLoadOrder.PostLoad + 1)]
public class BotLootLoader(BotTable bots, BotGeneratorHelper botGeneratorHelper, ItemHelper itemHelper, ISptLogger<BotLootLoader> log) : IOnLoad
{
    static readonly Dictionary<string, List<MongoId>> Ensure = new(StringComparer.Ordinal);
    static BotGeneratorHelper _botHelper;
    static ItemHelper _items;
    static ISptLogger<BotLootLoader> _log;

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        _botHelper = botGeneratorHelper; _items = itemHelper; _log = log;
        foreach (var p in ContentPacks.All(log))
            foreach (var file in PackLayout.DataFiles(p, "botloot"))
            {
                var label = p.Name + "/" + Path.GetFileName(file);
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllBytes(file), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                    foreach (var role in doc.RootElement.EnumerateObject())
                    {
                        if (role.Value.ValueKind != JsonValueKind.Object) continue;
                        var key = role.Name.ToLowerInvariant();
                        if (!bots.Types.TryGetValue(key, out var type) || type?.BotInventory?.Items == null || type.BotGeneration?.Items == null)
                        { log.Error($"[VisitAPI] botloot {label}: bot type '{role.Name}' not found on this server, skipped"); continue; }
                        if (role.Value.TryGetProperty("ensure", out var ensure) && ensure.ValueKind == JsonValueKind.Array)
                            foreach (var tpl in ensure.EnumerateArray())
                                if (tpl.ValueKind == JsonValueKind.String && tpl.GetString()?.Length == 24)
                                    (Ensure.TryGetValue(key, out var list) ? list : Ensure[key] = new List<MongoId>()).Add(new MongoId(tpl.GetString()));
                        // 口袋 / 背包 / 胸挂的物品池：照原生权重抽（10-06 SORA「reshala身上的钥匙改为和正式版一样概率刷新」）
                        foreach (var (field, get) in new (string, Func<ItemPools, Dictionary<MongoId, double>>)[]
                                 {
                                     ("pockets", ip => ip.Pockets ??= new Dictionary<MongoId, double>()),
                                     ("backpack", ip => ip.Backpack ??= new Dictionary<MongoId, double>()),
                                     ("vest", ip => ip.TacticalVest ??= new Dictionary<MongoId, double>()),
                                 })
                            if (role.Value.TryGetProperty(field, out var weights) && weights.ValueKind == JsonValueKind.Object)
                            {
                                var dict = get(type.BotInventory.Items);
                                foreach (var item in weights.EnumerateObject())
                                    if (item.Name.Length == 24 && item.Value.ValueKind == JsonValueKind.Number)
                                        dict[new MongoId(item.Name)] = item.Value.GetDouble();
                            }
                        if (role.Value.TryGetProperty("specialLoot", out var pool) && pool.ValueKind == JsonValueKind.Object)
                        {
                            type.BotInventory.Items.SpecialLoot ??= new Dictionary<MongoId, double>();
                            foreach (var item in pool.EnumerateObject())
                                if (item.Name.Length == 24 && item.Value.ValueKind == JsonValueKind.Number)
                                    type.BotInventory.Items.SpecialLoot[new MongoId(item.Name)] = item.Value.GetDouble();
                        }
                        if (role.Value.TryGetProperty("specialItems", out var counts) && counts.ValueKind == JsonValueKind.Object && type.BotGeneration.Items.SpecialItems != null)
                        {
                            var weights = new Dictionary<double, double>();
                            foreach (var c in counts.EnumerateObject())
                                if (double.TryParse(c.Name, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) && c.Value.ValueKind == JsonValueKind.Number)
                                    weights[n] = c.Value.GetDouble();
                            if (weights.Count > 0) type.BotGeneration.Items.SpecialItems.Weights = weights;
                        }
                    }
                }
                catch (Exception e) { log.Error($"[VisitAPI] botloot {label}: could not be read, skipped: {e.Message}"); }
            }
        if (Ensure.Count > 0)
        {
            var patch = new EnsureLootPatch();
            try
            {
                patch.Enable();
                if (!patch.IsActive) log.Error("[VisitAPI] botloot 'ensure' did NOT arm (IsActive=false after Enable); those bots only carry the item when it happens to fit");
            }
            catch (Exception e) { log.Error("[VisitAPI] botloot 'ensure' failed to arm (those bots only carry the item when it happens to fit): " + e); }
        }
        return Task.CompletedTask;
    }

    public class EnsureLootPatch : AbstractPatch
    {
        protected override MethodBase GetTargetMethod() =>
            typeof(BotLootGenerator).GetMethod(nameof(BotLootGenerator.GenerateLoot), BindingFlags.Instance | BindingFlags.Public);

        /// 参数名与目标方法一致（botId / botGenerationDetails / botInventory），补丁框架按名字喂
        [PatchPostfix]
        public static void Postfix(MongoId botId, BotGenerationDetails botGenerationDetails, BotBaseInventory botInventory)
        {
            try
            {
                var role = botGenerationDetails?.RoleLowercase;
                if (role == null || botInventory?.Items == null || !Ensure.TryGetValue(role, out var wanted)) return;
                foreach (var tpl in wanted)
                {
                    if (botInventory.Items.Any(i => i.Template == tpl)) continue;
                    var template = _items.GetItem(tpl).Value;
                    var item = new Item { Id = new MongoId(), Template = tpl, Upd = _botHelper.GenerateExtraPropertiesForItem(template, role, true) };
                    var slots = new HashSet<EquipmentSlots> { EquipmentSlots.Pockets, EquipmentSlots.TacticalVest, EquipmentSlots.Backpack };
                    if (_botHelper.AddItemWithChildrenToEquipmentSlot(botId, slots, item.Id, tpl, new List<Item> { item }, botInventory) == ItemAddedResult.SUCCESS) continue;
                    // 放不下：口袋里挑一件（优先备用弹匣）原地换掉
                    var equipment = botInventory.Equipment?.ToString();
                    var pockets = botInventory.Items.FirstOrDefault(i => i.SlotId == "Pockets" && i.ParentId == equipment);
                    if (pockets == null) { _log?.Warning($"[VisitAPI] botloot: {role} has no pockets, could not give it {tpl}"); continue; }
                    var pocketsId = pockets.Id.ToString();
                    var victim = botInventory.Items.Where(i => i.ParentId == pocketsId)
                        .OrderByDescending(i => _items.IsOfBaseclass(i.Template, BaseClasses.MAGAZINE)).FirstOrDefault();
                    if (victim == null) { _log?.Warning($"[VisitAPI] botloot: {role}'s pockets are empty but {tpl} still did not fit"); continue; }
                    var gone = new HashSet<string> { victim.Id.ToString() };
                    for (var grew = true; grew;)
                    {
                        grew = false;
                        foreach (var i in botInventory.Items)
                            if (i.ParentId != null && gone.Contains(i.ParentId) && gone.Add(i.Id.ToString())) grew = true;
                    }
                    botInventory.Items.RemoveAll(i => gone.Contains(i.Id.ToString()));
                    item.ParentId = pocketsId; item.SlotId = victim.SlotId; item.Location = victim.Location;
                    botInventory.Items.Add(item);
                }
            }
            catch (Exception e) { _log?.Warning("[VisitAPI] botloot: giving a bot its required item failed (bot generated without it): " + e.Message); }
        }
    }
}
