using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Utils;

namespace VisitAPI.Server;

[Injectable]
public class DialogueRouter(JsonUtil jsonUtil, HttpResponseUtil httpResponse)
    : StaticRouter(jsonUtil, [
        new RouteAction("/visitapi/dialogue/confirm",
            async (url, info, sessionId, output, ct) => httpResponse.GetBody(DialogueConfirmations.Payload()),
            typeof(EmptyRequestData))
    ]);
