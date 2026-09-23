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
            var flipped = new List<string>();
            foreach (var t in storyTraders)
            {
                var b = traders.GetTrader(new MongoId(t))?.Base;
                if (b == null || b.UnlockedByDefault != true) continue;
                b.UnlockedByDefault = false;
                flipped.Add(b.Nickname ?? t);
            }
        }
        catch (Exception e) { log.Error("[VisitAPI] story locks: could not change trader defaults: " + e); }
        var patch = new ResetTradersPatch();
        try
        {
            patch.Enable();
            if (!patch.IsActive) log.Error("[VisitAPI] story locks did NOT arm (IsActive=false after Enable)");
        }
        catch (Exception e) { log.Error("[VisitAPI] story locks failed to arm: " + e); }
        return Task.CompletedTask;
    }

    static (HashSet<string> traders, int mapQuests) Scan()
    {
        var traders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var maps = 0;
        foreach (var q in _templates?.Quests?.Values ?? Enumerable.Empty<SPTarkov.Server.Core.Models.Eft.Common.Tables.Quest>())
        {
            if (!(q.ExtensionData != null && q.ExtensionData.TryGetValue("visitapi", out var v) && v is JsonElement vx && vx.ValueKind == JsonValueKind.Object)) continue;
            foreach (var list in q.Rewards?.Values ?? Enumerable.Empty<List<SPTarkov.Server.Core.Models.Eft.Common.Tables.Reward>>())
                foreach (var r in list ?? new List<SPTarkov.Server.Core.Models.Eft.Common.Tables.Reward>())
                    if (r?.Type == RewardType.TraderUnlock && r.Target?.Length == 24) traders.Add(r.Target);
            if (vx.TryGetProperty("unlockLocations", out var ul) && ul.ValueKind == JsonValueKind.Array && ul.GetArrayLength() > 0) maps++;
        }
        return (traders, maps);
    }

    internal static void OnNewProfile(MongoId sessionId)
    {
        try
        {
            var (traders, mapQuests) = Scan();
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
