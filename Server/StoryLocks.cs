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
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Profile;

namespace VisitAPI.Server;

[Injectable(typePriority: OnLoadOrder.PostLoad + 10)]
public class StoryLocks(TemplateTable templates, TradersTable traders, ProfileHelper profileHelper, ISptLogger<StoryLocks> log) : IOnLoad
{
    public const string FlagVariable = "766973697461706900000001";

    static TemplateTable _templates;
    static ProfileHelper _profiles;
    static ISptLogger<StoryLocks> _log;

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        _templates = templates; _profiles = profileHelper; _log = log;
        try
        {
            var (storyTraders, _) = Scan();
            foreach (var t in storyTraders)
                if (traders.GetTrader(new MongoId(t))?.Base is { UnlockedByDefault: true } b) b.UnlockedByDefault = false;
        }
        catch (Exception e) { log.Error("[VisitAPI] story locks: could not change trader defaults: " + e); }
        Patches.Arm(new ResetTradersPatch(), log, "story locks");
        return Task.CompletedTask;
    }

    /// 带 visitapi 的任务里：解锁奖励点名的商人（剧情商人），和写了 unlockLocations 的任务数
    sealed record StoryScan(HashSet<string> Traders, int MapQuests);

    static StoryScan Scan()
    {
        var traders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var maps = 0;
        foreach (var q in _templates?.Quests?.Values ?? Enumerable.Empty<Quest>())
        {
            if (Ext.Visit(q.ExtensionData) is not { } vx) continue;
            foreach (var list in q.Rewards?.Values ?? Enumerable.Empty<List<Reward>>())
                foreach (var r in list ?? new List<Reward>())
                    if (r?.Type == RewardType.TraderUnlock && r.Target?.Length == 24) traders.Add(r.Target);
            if (vx.TryGetProperty("unlockLocations", out var ul) && ul.ValueKind == JsonValueKind.Array && ul.GetArrayLength() > 0) maps++;
        }
        return new StoryScan(traders, maps);
    }

    /// 新档用的扫描结果。开服以后任务表不再变，第一次建档时扫一遍记下、以后直接用；
    /// 不拿 OnLoadAsync 那一遍：那是 PostLoad+10，后面还有别的模组的 IOnLoad，第一次建档时任务表才肯定定了，结果和每次重扫一样
    static StoryScan _newProfileScan;

    internal static void OnNewProfile(MongoId sessionId)
    {
        try
        {
            var (traders, mapQuests) = _newProfileScan ??= Scan();
            if (traders.Count == 0 && mapQuests == 0) return;
            var pmc = _profiles?.GetPmcProfile(sessionId);
            if (pmc == null) { _log?.Warning($"[VisitAPI] story locks: new profile {sessionId} has no pmc, skipped"); return; }
            foreach (var t in traders)
                if (pmc.TradersInfo != null && pmc.TradersInfo.TryGetValue(new MongoId(t), out var info) && info != null)
                    info.Unlocked = false;
            pmc.Variables ??= new Dictionary<MongoId, int>();
            pmc.Variables[new MongoId(FlagVariable)] = 1;
        }
        catch (Exception e) { _log?.Error("[VisitAPI] story locks failed on new profile (profile left as SPT made it): " + e); }
    }

    public class ResetTradersPatch : AbstractPatch
    {
        protected override MethodBase GetTargetMethod() =>
            typeof(CreateProfileService).GetMethod("ResetAllTradersInProfile", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        [PatchPostfix]
        public static void Postfix(MongoId sessionId) => OnNewProfile(sessionId);
    }
}
