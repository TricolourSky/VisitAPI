using System.Text.Json.Serialization;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace VisitAPI.Server;

public class StandingRequest : IRequestData
{
    [JsonPropertyName("traderId")] public string TraderId { get; set; }
    [JsonPropertyName("delta")] public double Delta { get; set; }
}

[Injectable]
public class StandingRouter(JsonUtil jsonUtil, TraderHelper traderHelper, ProfileHelper profileHelper, HttpResponseUtil httpResponse, ISptLogger<StandingRouter> log)
    : StaticRouter(jsonUtil, [
        new RouteAction("/visitapi/standing/add",
            async (url, info, sessionId, output, ct) =>
            {
                var request = (StandingRequest)info;
                // 24 位但不是十六进制的 id 会让 new MongoId 抛异常，先校验（同 VariableRouter）
                if (request.TraderId?.Length == 24 && MongoId.IsValidMongoId(request.TraderId) && double.IsFinite(request.Delta))
                {
                    var traderId = new MongoId(request.TraderId);
                    // SPT 的 AddStandingToTrader 用索引器取档案里的商人，不在就抛、请求 500：.dlg 写了这份档案没有的商人（打错 / 对应的包没装）时点名（10-29）
                    if (profileHelper.GetPmcProfile(sessionId)?.TradersInfo?.ContainsKey(traderId) == true) traderHelper.AddStandingToTrader(sessionId, traderId, request.Delta);
                    else log.Warning($"[VisitAPI] standing: trader {request.TraderId} is not in profile {sessionId}; the .dlg standing effect was ignored");
                }
                return httpResponse.EmptyResponse();
            },
            typeof(StandingRequest))
    ]);
