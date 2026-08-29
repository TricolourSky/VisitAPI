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

/// <summary>.dlg 的 `set:` 记号落到 pmc.Variables（客户端登录时随 profile 下发回 ProfileVariables，闭环）。</summary>
[Injectable]
public class VariableRouter(JsonUtil jsonUtil, ProfileHelper profileHelper, HttpResponseUtil httpResponse)
    : StaticRouter(jsonUtil, [
        new RouteAction("/visitapi/variable/set",
            async (url, info, sessionId, output, ct) =>
            {
                var request = (VariableRequest)info;
                var pmc = profileHelper.GetPmcProfile(sessionId);
                if (pmc != null && request.VariableId?.Length == 24)
                {
                    pmc.Variables ??= new Dictionary<MongoId, int>();
                    pmc.Variables[new MongoId(request.VariableId)] = request.Value;
                }
                return httpResponse.EmptyResponse();
            },
            typeof(VariableRequest))
    ]);
