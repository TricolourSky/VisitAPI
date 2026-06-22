using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;

namespace VisitApiServer;

// SPT's QuestHelper.SendSuccessDialogMessageOnQuestComplete sends the quest-success mail UNCONDITIONALLY — unlike
// FailQuest, which guards on a non-empty fail message. VisitAPI quests are dialogue-driven and reward XP/standing
// (applied straight to the profile, never mailed), so a completed VisitAPI quest with an empty success message and
// no item rewards would still spawn an EMPTY trader mail. We Harmony-patch that one method to skip when the mail
// would carry nothing — empty success text AND no reward items — i.e. the same guard FailQuest already has. Quests
// with a success message OR item rewards are untouched, so item rewards are still delivered via their mail.
[Injectable]
public class VisitApiMailGuard : IOnLoad
{
    private static ISptLogger<VisitApiMailGuard>? _logger;

    public VisitApiMailGuard(ISptLogger<VisitApiMailGuard> logger) => _logger = logger;

    public Task OnLoad()
    {
        try
        {
            MethodInfo? target = AccessTools.Method(typeof(QuestHelper), "SendSuccessDialogMessageOnQuestComplete");
            if (target == null)
            {
                _logger?.Warning("[VisitAPI-Server] mail guard: QuestHelper.SendSuccessDialogMessageOnQuestComplete not found; empty quest mails will still send");
                return Task.CompletedTask;
            }
            new Harmony("com.sora.visitapi.server").Patch(target,
                prefix: new HarmonyMethod(typeof(VisitApiMailGuard), nameof(SkipEmptyQuestSuccessMail)));
            _logger?.Debug("[VisitAPI-Server] mail guard installed (suppress empty quest-success mails)");
        }
        catch (Exception ex)
        {
            _logger?.Error("[VisitAPI-Server] mail guard install failed: " + ex);
        }
        return Task.CompletedTask;
    }

    // Harmony prefix. Return false to SKIP the original (send no mail) — but ONLY when the mail would be empty:
    // no reward items to deliver AND an empty success message. Otherwise return true so the mail sends normally
    // (item rewards are delivered through that mail, so we must never skip when there are items).
    private static bool SkipEmptyQuestSuccessMail(QuestHelper __instance, PmcData pmcData, MongoId completedQuestId, List<Item> questRewards)
    {
        try
        {
            if (questRewards != null && questRewards.Count > 0) return true; // has item rewards -> mail must send

            Quest? quest = __instance.GetQuestFromDb(completedQuestId, pmcData);
            if (quest == null || string.IsNullOrWhiteSpace(quest.SuccessMessageText))
            {
                _logger?.Debug("[VisitAPI-Server] suppressed empty quest-success mail for " + completedQuestId);
                return false; // skip: nothing to deliver, no text -> an empty mail (XP/standing already applied)
            }
        }
        catch (Exception ex)
        {
            _logger?.Warning("[VisitAPI-Server] mail guard prefix error (mailing normally): " + ex.Message);
        }
        return true;
    }
}
