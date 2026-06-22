using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services.Mod;
using SPTarkov.Server.Core.Utils;
using Path = System.IO.Path;

namespace VisitApiServer;

// On server load, register every quest authored under db/quests/*.json (with locales from db/locales/<lang>.json)
// through SPT's official CustomQuestService.CreateQuest. Quests are standard EFT quest JSON; VisitAPI's dialog mod
// drives accept/complete client-side.
//
// TypePriority 650000 is CRITICAL. App runs IOnLoad components ordered by Injectable TypePriority; the default is
// int.MaxValue (dead last). SaveCallbacks (700000) loads AND validates every saved profile against the item DB.
// Our assort injector clones the custom weapon (e.g. MOD.X1) into the item DB — if that runs AFTER SaveCallbacks,
// a profile already holding the cloned weapon is validated before the tpl exists → SPT marks it invalid
// ("item not in database"). 650000 runs after Database(200000)/TraderRegistration(500000)/Handbook(600000) — so
// quest+assort injection still see registered traders — but BEFORE profile validation. (CreateItemFromClone only
// needs the Database stage; it has no Preset/Ragfair dependency.)
[Injectable(InjectionType.Scoped, null, 650000)]
public class VisitApiQuestLoader : IOnLoad
{
    private readonly ISptLogger<VisitApiQuestLoader> _logger;
    private readonly JsonUtil _jsonUtil;
    private readonly ModHelper _modHelper;
    private readonly CustomQuestService _customQuestService;
    private readonly VisitApiAssortInjector _assortInjector;

    public VisitApiQuestLoader(ISptLogger<VisitApiQuestLoader> logger, JsonUtil jsonUtil, ModHelper modHelper, CustomQuestService customQuestService, VisitApiAssortInjector assortInjector)
    {
        _logger = logger;
        _jsonUtil = jsonUtil;
        _modHelper = modHelper;
        _customQuestService = customQuestService;
        _assortInjector = assortInjector;
    }

    public Task OnLoad()
    {
        try
        {
            string modFolder = _modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
            string questsDir = Path.Combine(modFolder, "db", "quests");
            string localesDir = Path.Combine(modFolder, "db", "locales");

            // locales: db/locales/<lang>.json -> { localeKey: text }
            var locales = new Dictionary<string, Dictionary<string, string>>();
            if (Directory.Exists(localesDir))
            {
                foreach (string file in Directory.GetFiles(localesDir, "*.json"))
                {
                    string lang = Path.GetFileNameWithoutExtension(file);
                    var dict = _jsonUtil.Deserialize<Dictionary<string, string>>(_modHelper.GetRawFileData(localesDir, Path.GetFileName(file)));
                    if (dict != null) locales[lang] = dict;
                }
            }

            if (!Directory.Exists(questsDir))
            {
                _logger.Warning("[VisitAPI-Server] no db/quests folder found; nothing to register");
                return Task.CompletedTask;
            }

            int ok = 0, fail = 0;
            foreach (string file in Directory.GetFiles(questsDir, "*.json"))
            {
                var quests = _jsonUtil.Deserialize<Dictionary<string, Quest>>(_modHelper.GetRawFileData(questsDir, Path.GetFileName(file)));
                if (quests == null) continue;
                foreach (var kv in quests)
                {
                    var result = _customQuestService.CreateQuest(new NewQuestDetails { NewQuest = kv.Value, Locales = locales });
                    if (result.Success)
                    {
                        ok++;
                        _logger.Debug($"[VisitAPI-Server] registered quest {kv.Value.Id}");
                    }
                    else
                    {
                        fail++;
                        _logger.Error($"[VisitAPI-Server] quest {kv.Value.Id} failed: {string.Join("; ", result.Errors)}");
                    }
                }
            }
            _logger.Debug($"[VisitAPI-Server] quest registration complete: {ok} ok, {fail} failed");

            // Quests now exist in the DB → inject quest-gated trader assorts (e.g. SORA's M700 unlock). Done here,
            // after CreateQuest, so the AssortmentUnlock reward can attach to the freshly-registered quest.
            _assortInjector.Inject();
        }
        catch (Exception ex)
        {
            _logger.Error($"[VisitAPI-Server] OnLoad error: {ex}");
        }
        return Task.CompletedTask;
    }
}
