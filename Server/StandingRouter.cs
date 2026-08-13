using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
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
public class StandingRouter(JsonUtil jsonUtil, TraderHelper traderHelper, HttpResponseUtil httpResponse)
    : StaticRouter(jsonUtil, [
        new RouteAction("/visitapi/standing/add",
            async (url, info, sessionId, output, ct) =>
            {
                var request = (StandingRequest)info;
                traderHelper.AddStandingToTrader(sessionId, new MongoId(request.TraderId), request.Delta);
                return httpResponse.EmptyResponse();
            },
            typeof(StandingRequest))
    ]);
