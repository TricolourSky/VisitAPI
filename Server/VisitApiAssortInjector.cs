using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Services.Mod;
using SPTarkov.Server.Core.Utils;
using Path = System.IO.Path;

namespace VisitApiServer;

// Make a trader sell a specific item, gated behind a quest — pure SPT-native (no WTT API): we only append to the
// standard trader tables in DatabaseService, which look identical for vanilla and WTT/custom traders.
//   1) assort.Items + barter_scheme(价格) + loyal_level_items(好感度)  -> the goods on the shelf
//   2) questAssort["success"][rootId] = questId                        -> hide until that quest is Success
//      (SPT's AssortHelper.StripLockedQuestAssort iterates loyal_level_items keys and removes any whose
//       questassort gate isn't met — verified against SPT 4.0.13 source.)
//   3) an AssortmentUnlock reward on the quest's Success list          -> the "解锁购买" line on the quest screen
//      (RewardHelper no-ops AssortmentUnlock server-side, so this is display-only and never double-adds the item.)
// Driven by db/assort/*.json. Called from VisitApiQuestLoader AFTER quests are registered, so the quest exists when
// we attach the reward; the trader is already present because WTT registers it during the DB load phase.
[Injectable]
public class VisitApiAssortInjector
{
    private readonly ISptLogger<VisitApiAssortInjector> _logger;
    private readonly JsonUtil _jsonUtil;
    private readonly ModHelper _modHelper;
    private readonly DatabaseService _databaseService;
    private readonly CustomItemService _customItemService;

    public VisitApiAssortInjector(ISptLogger<VisitApiAssortInjector> logger, JsonUtil jsonUtil, ModHelper modHelper, DatabaseService databaseService, CustomItemService customItemService)
    {
        _logger = logger;
        _jsonUtil = jsonUtil;
        _modHelper = modHelper;
        _databaseService = databaseService;
        _customItemService = customItemService;
    }

    public void Inject()
    {
        try
        {
            string modFolder = _modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
            string assortDir = Path.Combine(modFolder, "db", "assort");
            if (!Directory.Exists(assortDir))
            {
                return; // no assorts to inject — that's fine
            }
            foreach (string file in Directory.GetFiles(assortDir, "*.json"))
            {
                InjectFile(assortDir, Path.GetFileName(file));
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"[VisitAPI-Server] assort inject error: {ex}");
        }
    }

    private void InjectFile(string dir, string fileName)
    {
        string raw = _modHelper.GetRawFileData(dir, fileName);
        var cfg = _jsonUtil.Deserialize<SoraAssortFile>(raw);
        if (cfg == null || cfg.Items.Count == 0 || string.IsNullOrEmpty(cfg.RootId))
        {
            _logger.Warning($"[VisitAPI-Server] {fileName}: empty/invalid assort file, skipped");
            return;
        }

        var trader = _databaseService.GetTrader(new MongoId(cfg.TraderId));
        if (trader == null)
        {
            // The trader isn't in the DB yet — WTT/BinaryDimension should register it during DB load, before us.
            _logger.Warning($"[VisitAPI-Server] {fileName}: trader {cfg.TraderId} not found, skipped (load order? trader not registered yet)");
            return;
        }

        if (trader.QuestAssort == null)
        {
            // No quest-gate dictionary to write into — bail rather than sell the item ungated.
            _logger.Warning($"[VisitAPI-Server] {fileName}: trader {cfg.TraderId} has no questAssort; skipped (refusing to sell an item with no quest gate)");
            return;
        }

        MongoId root = new MongoId(cfg.RootId);

        // 0) optional custom shop name: clone the weapon's base tpl into a new named item (e.g. MOD.X1) and point
        // the assort root at it. The trader card shows the new tpl's locale name — server-side, no client preset.
        var rootItem = cfg.Items.FirstOrDefault(i => ((string)i.Id) == cfg.RootId) ?? cfg.Items[0];
        string weaponTpl = (string)rootItem.Template;
        if (!string.IsNullOrEmpty(cfg.WeaponName) && !string.IsNullOrEmpty(cfg.WeaponTplId))
        {
            EnsureClonedWeapon((string)rootItem.Template, cfg.WeaponTplId!, cfg.WeaponName!, cfg.Price);
            weaponTpl = cfg.WeaponTplId!;
            rootItem.Template = new MongoId(weaponTpl);
        }

        // 1) goods on the shelf
        trader.Assort.Items.AddRange(cfg.Items);
        trader.Assort.BarterScheme[root] = new List<List<BarterScheme>>
        {
            new() { new BarterScheme { Template = new MongoId(cfg.CurrencyTpl), Count = cfg.Price } }
        };
        trader.Assort.LoyalLevelItems[root] = cfg.LoyaltyLevel;

        // 2) quest gate: only visible once the quest is Success
        if (!trader.QuestAssort.TryGetValue("success", out var successGate) || successGate == null)
        {
            successGate = new Dictionary<MongoId, MongoId>();
            trader.QuestAssort["success"] = successGate;
        }
        successGate[root] = new MongoId(cfg.QuestId);

        // 3) the quest-screen "unlock purchase" line (display only)
        AddUnlockReward(cfg, raw, weaponTpl);

        _logger.Debug($"[VisitAPI-Server] {fileName}: injected {cfg.Items.Count} assort item(s) into {cfg.TraderId}, unlocked by quest {cfg.QuestId} (price {cfg.Price}, LL{cfg.LoyaltyLevel})");
    }

    private void AddUnlockReward(SoraAssortFile cfg, string raw, string weaponTpl)
    {
        var quest = _databaseService.GetQuests().GetValueOrDefault(new MongoId(cfg.QuestId));
        if (quest == null)
        {
            _logger.Warning($"[VisitAPI-Server] quest {cfg.QuestId} not in DB; skipped AssortmentUnlock reward (quest registration order?)");
            return;
        }

        // Independent copy of the tree for the reward (don't mutate the assort items). A reward's root item is
        // standalone, so strip its hideout parent/slot; and point it at the renamed tpl so the unlock line shows it too.
        var rewardItems = _jsonUtil.Deserialize<SoraAssortFile>(raw)?.Items ?? new List<Item>();
        var rewardRoot = rewardItems.FirstOrDefault(i => (string)i.Id == cfg.RootId);
        if (rewardRoot != null)
        {
            rewardRoot.ParentId = null;
            rewardRoot.SlotId = null;
            rewardRoot.Template = new MongoId(weaponTpl);
        }

        quest.Rewards ??= new Dictionary<string, List<Reward>>();
        if (!quest.Rewards.TryGetValue("Success", out var successRewards) || successRewards == null)
        {
            successRewards = new List<Reward>();
            quest.Rewards["Success"] = successRewards;
        }
        successRewards.Add(new Reward
        {
            Id = new MongoId(),
            Index = successRewards.Count,
            Type = RewardType.AssortmentUnlock,
            Target = cfg.RootId,
            TraderId = cfg.TraderId,
            LoyaltyLevel = cfg.LoyaltyLevel,
            Items = rewardItems,
            Unknown = false
        });
    }

    // Clone a weapon's base template into a new item tpl with a custom shop name (idempotent within a server run).
    // SPT's CustomItemService.CreateItemFromClone copies _props (so the M700's mods still fit), and registers the
    // new tpl in items DB + handbook + EVERY language's locale ("{id} Name/ShortName/Description") — so the trader
    // card, inventory and inspector all show the custom name. No client preset-matching involved.
    private void EnsureClonedWeapon(string baseTpl, string newTpl, string weaponName, int fallbackPrice)
    {
        var items = _databaseService.GetItems();
        if (items.ContainsKey(new MongoId(newTpl))) return;
        var baseItem = items.GetValueOrDefault(new MongoId(baseTpl));
        if (baseItem == null)
        {
            _logger.Warning($"[VisitAPI-Server] base weapon {baseTpl} not in item DB; cannot clone/rename (shop name stays default)");
            return;
        }
        var hb = _databaseService.GetTemplates().Handbook.Items.FirstOrDefault(i => ((string)i.Id) == baseTpl);
        var details = new NewItemFromCloneDetails
        {
            ItemTplToClone = new MongoId(baseTpl),
            NewId = newTpl,
            ParentId = (string)baseItem.Parent,                    // item-type parent (e.g. SniperRifle) — same as base
            HandbookParentId = hb != null ? (string)hb.ParentId : null,
            HandbookPriceRoubles = hb?.Price ?? fallbackPrice,
            FleaPriceRoubles = hb?.Price ?? fallbackPrice,
            Locales = new Dictionary<string, LocaleDetails>
            {
                ["en"] = new LocaleDetails { Name = weaponName, ShortName = weaponName, Description = weaponName + " — custom weapon." },
                ["ch"] = new LocaleDetails { Name = weaponName, ShortName = weaponName, Description = weaponName + " —— 自定义武器。" },
            },
        };
        var r = _customItemService.CreateItemFromClone(details);
        if (r.Success == true) _logger.Debug($"[VisitAPI-Server] cloned weapon {baseTpl} -> {newTpl} renamed to '{weaponName}'");
        else _logger.Warning($"[VisitAPI-Server] 克隆武器失败: {string.Join("; ", r.Errors ?? new List<string>())}");
    }

    // db/assort/*.json shape: a small config header + the货架 item tree (root first).
    private sealed record SoraAssortFile
    {
        [JsonPropertyName("traderId")] public string TraderId { get; init; } = "";
        [JsonPropertyName("questId")] public string QuestId { get; init; } = "";
        [JsonPropertyName("currencyTpl")] public string CurrencyTpl { get; init; } = "";
        [JsonPropertyName("price")] public int Price { get; init; }
        [JsonPropertyName("loyaltyLevel")] public int LoyaltyLevel { get; init; } = 1;
        [JsonPropertyName("rootId")] public string RootId { get; init; } = "";
        // Optional: rename the sold weapon. weaponTplId is a fixed new tpl cloned from the root item's _tpl.
        [JsonPropertyName("weaponName")] public string? WeaponName { get; init; }
        [JsonPropertyName("weaponTplId")] public string? WeaponTplId { get; init; }
        [JsonPropertyName("items")] public List<Item> Items { get; init; } = new();
    }
}
