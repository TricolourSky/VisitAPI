using System.Collections.Generic;
using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace VisitAPI.Server;

public class VariableRequest : IRequestData
{
    [JsonPropertyName("variableId")] public string VariableId { get; set; }
    [JsonPropertyName("value")] public int Value { get; set; }
}

[Injectable]
public class VariableRouter(JsonUtil jsonUtil, ProfileHelper profileHelper, HttpResponseUtil httpResponse)
    : StaticRouter(jsonUtil, [
        new RouteAction("/visitapi/variable/set",
            async (url, info, sessionId, output, ct) =>
            {
                var request = (VariableRequest)info;
                var pmc = profileHelper.GetPmcProfile(sessionId);
                // 24 位但不是十六进制的 id 会让 new MongoId 抛异常，先校验（09-24 审查 M1）
                if (pmc != null && request?.VariableId?.Length == 24 && MongoId.IsValidMongoId(request.VariableId))
                    lock (ProfileVariableLock.For(sessionId))
                    {
                        pmc.Variables ??= new Dictionary<MongoId, int>();
                        pmc.Variables[new MongoId(request.VariableId)] = request.Value;
                        VariableGroups.Recompute(pmc.Variables);
                    }
                return httpResponse.EmptyResponse();
            },
            typeof(VariableRequest)),
        new RouteAction("/visitapi/variable/groups",
            async (url, info, sessionId, output, ct) =>
            {
                var pmc = profileHelper.GetPmcProfile(sessionId);
                if (pmc?.Variables != null)
                    lock (ProfileVariableLock.For(sessionId)) VariableGroups.Recompute(pmc.Variables);
                return httpResponse.GetBody(VariableGroups.Payload());
            },
            typeof(VariableRequest))
    ]);
