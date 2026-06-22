using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;

namespace VisitApiServer;

// The hideout has NO client-side quest controller, so VisitAPI cannot complete a quest there. This route lets
// the client ask the server to finish a quest: set it Success in the profile + apply its rewards (XP) WITHOUT
// going through MailSendService — so the trader sends NO mail. (The "native static router" from the plan.)
[Injectable]
public class VisitApiQuestRouter : StaticRouter
{
    // Demo: the SORA hideout quest. TODO generalise — pass the quest id from the client (url/body) once the
    // VisitAPI .dlg completion directive carries it across the HTTP call.
    private const string SoraQuestId = "5043a1ce90726f6a536f7261";

    public VisitApiQuestRouter(JsonUtil jsonUtil, HttpResponseUtil http, ProfileHelper profileHelper,
        QuestRewardHelper questRewardHelper, SaveServer saveServer, ISptLogger<VisitApiQuestRouter> logger)
        : base(jsonUtil, GetRoutes(http, profileHelper, questRewardHelper, saveServer, logger))
    {
    }

    private static List<RouteAction> GetRoutes(HttpResponseUtil http, ProfileHelper profileHelper,
        QuestRewardHelper questRewardHelper, SaveServer saveServer, ISptLogger<VisitApiQuestRouter> logger)
    {
        return new List<RouteAction>
        {
            new RouteAction(
                "/visitapi/quest/complete",
                async (string url, IRequestData info, MongoId sessionId, string? output) =>
                {
                    string result = await CompleteAsync(profileHelper, questRewardHelper, saveServer, logger, sessionId);
                    return (object)http.GetBody(result);
                })
        };
    }

    private static async Task<string> CompleteAsync(ProfileHelper profileHelper, QuestRewardHelper questRewardHelper,
        SaveServer saveServer, ISptLogger<VisitApiQuestRouter> logger, MongoId sessionId)
    {
        try
        {
            PmcData? pmc = profileHelper.GetPmcProfile(sessionId);
            if (pmc == null)
            {
                logger.Warning("[VisitAPI-Server] complete: no PMC profile for " + sessionId);
                return "no-profile";
            }
            MongoId qid = new MongoId(SoraQuestId);
            pmc.Quests ??= new List<QuestStatus>();
            QuestStatus? qs = pmc.Quests.Find(q => q.QId == qid);
            // Idempotent: if the quest is already Success, do NOT re-apply the reward (no double XP if the
            // hideout interaction fires again before the client-side gate hides it).
            if (qs != null && qs.Status == QuestStatusEnum.Success)
            {
                logger.Debug("[VisitAPI-Server] quest " + SoraQuestId + " already Success; reward not re-applied");
                return "already-success";
            }
            if (qs == null)
            {
                pmc.Quests.Add(new QuestStatus
                {
                    QId = qid,
                    StartTime = 0,
                    Status = QuestStatusEnum.Success,
                    StatusTimers = new Dictionary<QuestStatusEnum, double>()
                });
            }
            else
            {
                qs.Status = QuestStatusEnum.Success;
            }

            // Apply the quest's Success rewards (XP) to the profile. NOTE: deliberately NOT calling
            // MailSendService afterwards → the trader sends no mail.
            questRewardHelper.ApplyQuestReward(pmc, qid, QuestStatusEnum.Success, sessionId, new ItemEventRouterResponse());

            await saveServer.SaveProfileAsync(sessionId);
            logger.Debug("[VisitAPI-Server] completed quest " + SoraQuestId + " (+reward, no mail) for " + sessionId);
            return "completed";
        }
        catch (Exception ex)
        {
            logger.Error("[VisitAPI-Server] complete route error: " + ex);
            return "error: " + ex.Message;
        }
    }
}
