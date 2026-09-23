using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace VisitAPI.Server;

public class DialogueConfirmRequest : IRequestData { }

[Injectable]
public class DialogueRouter(JsonUtil jsonUtil, HttpResponseUtil httpResponse)
    : StaticRouter(jsonUtil, [
        new RouteAction("/visitapi/dialogue/confirm",
            async (url, info, sessionId, output, ct) => httpResponse.GetBody(DialogueConfirmations.Payload()),
            typeof(DialogueConfirmRequest))
    ]);
