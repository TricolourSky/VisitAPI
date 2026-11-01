using System;
using System.Collections.Generic;
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

namespace VisitAPI.Server;

/// <summary>内容包 botloot\*.json 按机器人类型改它身上的物品：{ "&lt;bot 类型，小写&gt;": { "pockets" / "backpack" / "vest": {物品: 权重}, "ensure": [物品 id…] } }
///   pockets / backpack / vest：权重加进原生物品池，原生照抽。10-06 意外证人 SORA 定「reshala身上的钥匙改为和正式版一样概率刷新」：
///   SPT5 的 Reshala 口袋池 33 件共 2400、钥匙 16（一个 Reshala 约 1%）；SPT 4.1 的池子 209 件共 143300，同比例换算成 962，概率与正式版一致。
///   ensure：必带。机器人生成完（SPT 4.1.6 BotLootGenerator.GenerateLoot 之后）身上没有就补一件：先照原生的放法试（口袋 / 胸挂 / 背包有空就放），
///   放不下（Reshala 没胸挂没背包，备用弹匣常先塞满口袋）就把口袋里的一件（优先备用弹匣）原地换成它——同格同位置，SPT 的容器占位缓存（BotInventoryContainerService）不用动。
///   10-17 SORA：ensure 留着，以后剧情要用。</summary>
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
        PackJson.Read(ContentPacks.All(log), "botloot", (label, root) =>
        {
            foreach (var role in root.EnumerateObject())
            {
                if (role.Value.ValueKind != JsonValueKind.Object) continue;
                var key = role.Name.ToLowerInvariant();
                if (!bots.Types.TryGetValue(key, out var type) || type?.BotInventory?.Items == null || type.BotGeneration?.Items == null)
                { log.Error($"[VisitAPI] botloot {label}: bot type '{role.Name}' not found on this server, skipped"); continue; }
                ApplyRole(key, type, role.Value);
            }
        }, (label, e) => log.Error($"[VisitAPI] botloot {label}: could not be read, skipped: {e.Message}"));
        if (Ensure.Count > 0) Patches.Arm(new EnsureLootPatch(), log, "botloot 'ensure'", "those bots only carry the item when it happens to fit");
        return Task.CompletedTask;
    }

    /// 一个兵种（key 是小写的类型名）：ensure 记下来等补丁补；pockets / backpack / vest 的权重直接写进它的物品池
    static void ApplyRole(string key, BotType type, JsonElement role)
    {
        if (role.TryGetProperty("ensure", out var ensure) && ensure.ValueKind == JsonValueKind.Array)
            foreach (var tpl in ensure.EnumerateArray())
                if (tpl.ValueKind == JsonValueKind.String && tpl.GetString()?.Length == 24)
                    (Ensure.TryGetValue(key, out var list) ? list : Ensure[key] = new List<MongoId>()).Add(new MongoId(tpl.GetString()));
        foreach (var (field, get) in new (string, Func<ItemPools, Dictionary<MongoId, double>>)[]
                 {
                     ("pockets", ip => ip.Pockets ??= new Dictionary<MongoId, double>()),
                     ("backpack", ip => ip.Backpack ??= new Dictionary<MongoId, double>()),
                     ("vest", ip => ip.TacticalVest ??= new Dictionary<MongoId, double>()),
                 })
            if (role.TryGetProperty(field, out var weights) && weights.ValueKind == JsonValueKind.Object)
            {
                var dict = get(type.BotInventory.Items);
                foreach (var item in weights.EnumerateObject())
                    if (item.Name.Length == 24 && item.Value.ValueKind == JsonValueKind.Number)
                        dict[new MongoId(item.Name)] = item.Value.GetDouble();
            }
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
