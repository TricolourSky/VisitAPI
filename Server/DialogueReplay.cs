using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI.Routing;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Inventory;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace VisitAPI.Server;

[Injectable(InjectionType.Transient, 300000)]
public sealed class DialogueReplayRouter(ProfileHelper profiles, TemplateTable templates, ISptLogger<DialogueReplayRouter> log)
    : ItemEventRouter([
        new ItemRouteAction<SaveDialogueStateRequest>("SaveDialogueState", (url, pmc, body, sessionId, output, ct) =>
        {
            try { Replay(profiles, templates, pmc, body, sessionId); }
            catch (Exception ex) { log.Error("[VisitAPI] dialogue replay failed (ignored): " + ex.Message); }
            return new ValueTask<ItemEventRouterResponse>(output);
        })
    ])
{
    static Dictionary<string, TraderDialogElement> _index;

    static void Replay(ProfileHelper profiles, TemplateTable templates, PmcData pmc, SaveDialogueStateRequest body, MongoId sessionId)
    {
        var full = profiles.GetFullProfile(sessionId);
        if (full != null) full.DialogueProgress = body.DialogueProgress;
        if (body.DialogueProgress == null || body.DialogueProgress.Count == 0 || pmc == null) return;
        _index ??= templates.Dialogue.Elements.GroupBy(e => e.Id.ToString()).ToDictionary(g => g.Key, g => g.First());
        // 和 /visitapi/variable/set 用同一把按会话的锁改 pmc.Variables（09-24 审查 M1）
        lock (ProfileVariableLock.For(sessionId))
        {
            pmc.Variables ??= new Dictionary<MongoId, int>();
            var persisted = 0;
            foreach (var step in body.DialogueProgress)
                if (step != null && step.DialogueId != null && step.NodeId != null && _index.TryGetValue(step.DialogueId, out var element))
                    persisted += Apply(element, step.NodeId, pmc.Variables);
            if (persisted > 0) VariableGroups.Recompute(pmc.Variables);
        }
    }

    static int Apply(TraderDialogElement element, string nodeId, Dictionary<MongoId, int> variables)
    {
        if (element.Lines == null) return 0;
        foreach (var line in element.Lines)
        {
            if (line is not JsonElement { ValueKind: JsonValueKind.Object } node || !node.TryGetProperty("Id", out var idProp) || idProp.GetString() != nodeId) continue;
            if (!node.TryGetProperty("Actions", out var actionsProp) || actionsProp.ValueKind != JsonValueKind.Array) return 0;
            var count = 0;
            foreach (var action in actionsProp.EnumerateArray())
            {
                if (action.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "SetVariable"
                    && action.TryGetProperty("saveScope", out var scopeProp) && scopeProp.GetString() == "Profile"
                    && action.TryGetProperty("variableId", out var variableIdProp)
                    && action.TryGetProperty("value", out var valueProp) && valueProp.TryGetInt32(out var intValue))
                {
                    var variableId = variableIdProp.GetString();
                    if (variableId != null && variableId.Length == 24 && MongoId.IsValidMongoId(variableId))
                    {
                        variables[variableId] = intValue;
                        count++;
                    }
                }
            }
            return count;
        }
        return 0;
    }
}
