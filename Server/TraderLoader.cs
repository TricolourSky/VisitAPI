using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;
using VisitAPI.Packs;
using Path = System.IO.Path;

namespace VisitAPI.Server;

[Injectable(typePriority: OnLoadOrder.TraderRegistration)]
public class TraderLoader(TradersTable traders, LocaleTable localeTable, ImageRouter images, TraderConfig traderConfig, JsonUtil json, ISptLogger<TraderLoader> log) : IOnLoad
{
    public static readonly List<MongoId> Registered = new();

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var owner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in ContentPacks.All(log))
        {
            foreach (var dir in PackLayout.TraderDirs(p))
            {
                var id = Path.GetFileName(dir);
                var label = p.Name + "/traders/" + id;
                if (!MongoId.IsValidMongoId(id)) { log.Error($"[VisitAPI] {label}: folder name is not a 24-char trader id, skipped"); continue; }
                if (owner.TryGetValue(id, out var first)) { log.Error($"[VisitAPI] Trader {id} exists in both pack {first} and {p.Name}, using the former"); continue; }
                if (traders.ContainsKey(new MongoId(id))) { owner[id] = "(already on this server)"; continue; }
                if (Register(dir, id, label)) owner[id] = p.Name;
            }
            AddLocales(p);
        }
        return Task.CompletedTask;
    }

    bool Register(string dir, string id, string label)
    {
        var baseFile = Path.Combine(dir, "base.json");
        if (!File.Exists(baseFile)) { log.Error($"[VisitAPI] {label}: no base.json"); return false; }
        TraderBase tb;
        try { tb = json.Deserialize<TraderBase>(File.ReadAllText(baseFile)); }
        catch (Exception e) { log.Error($"[VisitAPI] {label}: could not read base.json: {e.Message}"); return false; }
        if (tb == null) { log.Error($"[VisitAPI] {label}: base.json deserialization failed"); return false; }
        if (tb.Id.ToString() != id) { log.Error($"[VisitAPI] {label}: base.json _id ({tb.Id}) does not match the folder name"); return false; }
        var trader = new Trader
        {
            Base = tb,
            Assort = new TraderAssort { Items = new List<Item>(), BarterScheme = new Dictionary<MongoId, List<List<BarterScheme>>>(), LoyalLevelItems = new Dictionary<MongoId, int>(), NextResupply = tb.NextResupply },
            QuestAssort = new Dictionary<string, Dictionary<MongoId, MongoId>> { ["started"] = new(), ["success"] = new(), ["fail"] = new() },
            Dialogue = new Dictionary<string, List<string>>(),
        };
        traders[tb.Id] = trader;
        Registered.Add(tb.Id);
        if (!traderConfig.UpdateTime.Any(u => u.TraderId == tb.Id))
        {
            var seconds = traderConfig.UpdateTimeDefault > 0 ? traderConfig.UpdateTimeDefault : 3600;
            traderConfig.UpdateTime.Add(new UpdateTime { Name = tb.Nickname ?? id, TraderId = tb.Id, Seconds = new MinMax<int>(seconds, seconds) });
        }
        var avatar = Path.Combine(dir, "avatar.png");
        if (File.Exists(avatar) && !string.IsNullOrEmpty(tb.Avatar))
        {
            var key = tb.Avatar;
            var dot = key.LastIndexOf('.');
            if (dot > key.LastIndexOf('/')) key = key.Substring(0, dot);
            images.AddRoute(key, avatar);
        }
        else log.Warning($"[VisitAPI] {label}: no avatar.png or base.json has no avatar, chat and quest pages will show the default avatar");
        return true;
    }

    /// <summary>包文案里以「商人id 」开头的键（Nickname / FullName / FirstName / Location / Description）灌进每种语言：有该语言的用它，没有的退到 en，都没有就不加。</summary>
    void AddLocales(PackInfo p)
    {
        if (Registered.Count == 0) return;
        var prefixes = Registered.Select(id => id + " ").ToList();
        var byLang = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in PackLayout.DataFiles(p, "locales"))
        {
            Dictionary<string, string> table;
            try { table = json.DeserializeFromFile<Dictionary<string, string>>(file); }
            catch (Exception e) { log.Error($"[VisitAPI] {p.Name}/locales/{Path.GetFileName(file)} could not be read: {e.Message}"); continue; }
            if (table == null) continue;
            var picked = table.Where(kv => prefixes.Any(pre => kv.Key.StartsWith(pre, StringComparison.OrdinalIgnoreCase))).ToDictionary(kv => kv.Key, kv => kv.Value);
            if (picked.Count > 0) byLang[Path.GetFileNameWithoutExtension(file)] = picked;
        }
        if (byLang.Count == 0) return;
        byLang.TryGetValue("en", out var fallback);
        foreach (var (lang, lazy) in localeTable.Global)
        {
            var entries = byLang.TryGetValue(lang, out var own) ? own : fallback;
            if (entries == null || entries.Count == 0) continue;
            lazy.AddTransformer(data => { if (data != null) foreach (var (k, v) in entries) data.TryAdd(k, v); return data; });
        }
    }
}

/// <summary>老档案没有新商人的条目：SPT 只在建档时给全部商人建 TradersInfo，登录时只删多余的、不补缺的，而任务列表下发时
/// 「档案里没有这个商人」= 整条任务不发（QuestHelper 897 行，当初章节整个消失就是这个）。服务端起来、档案读完之后补齐，
/// 记录按 SPT 建档时的样子自己构造（不调 ResetTrader——它会顺手按档案模板重设跳蚤封禁）。新建的档案不用管，建档时表里已经有这些商人。</summary>
[Injectable(typePriority: OnLoadOrder.PostLoad)]
public class TraderProfiles(SaveServer saves, TradersTable traders, ISptLogger<TraderProfiles> log) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (TraderLoader.Registered.Count == 0) return Task.CompletedTask;
        foreach (var (sessionId, profile) in saves.GetProfiles())
        {
            var pmc = profile?.CharacterData?.PmcData;
            if (pmc?.Info?.Side == null || pmc.TradersInfo == null) continue;
            foreach (var id in TraderLoader.Registered)
            {
                if (pmc.TradersInfo.ContainsKey(id)) continue;
                var tb = traders.GetTrader(id)?.Base;
                if (tb == null) continue;
                try { pmc.TradersInfo.TryAdd(id, new TraderInfo { Disabled = false, LoyaltyLevel = 1, SalesSum = 0, Standing = 0, NextResupply = tb.NextResupply, Unlocked = tb.UnlockedByDefault }); }
                catch (Exception e) { log.Error($"[VisitAPI] Failed to add trader {id} to profile {sessionId}: {e.Message}"); }
            }
        }
        return Task.CompletedTask;
    }
}
