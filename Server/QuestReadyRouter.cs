using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Ws;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace VisitAPI.Server;

public class QuestReadyRequest : IRequestData { }

[Injectable]
public class QuestReadyRouter(JsonUtil jsonUtil, TemplateTable templates, HttpResponseUtil httpResponse)
    : StaticRouter(jsonUtil, [
        new RouteAction("/visitapi/quest/flags",
            async (url, info, sessionId, output, ct) =>
                httpResponse.GetBody(templates.Quests.Values
                    .Select(q => (id: q.Id.ToString(), vx: Visit(q.ExtensionData), notes: Notes(q), subs: Subs(q), story: Story(q.ExtensionData), noCounter: NoCounter(q), talkTo: TalkTo(q, templates), call: Call(q.ExtensionData), finishers: Finishers(q), hidden: Hidden(q.ExtensionData)))
                    .Select(x => new
                    {
                        x.id, x.notes, anyOf = AnyOf(x.vx), chapter = Flag(x.vx, "chapter"),
                        autoStart = Flag(x.vx, "autoStart"), autoFinish = Flag(x.vx, "autoFinish"), dialogOnly = Flag(x.vx, "dialogOnly"), icon = Str(x.vx, "icon"), items = Items(x.vx),
                        startAfter = Str(x.vx, "startAfter"), order = Num(x.vx, "order"), noteLinks = Obj(x.vx, "noteLinks"), x.subs, x.story, x.noCounter,
                        unlockDialogue = StrList(x.vx, "unlockDialogue"), x.talkTo, unlockLocations = StrList(x.vx, "unlockLocations"), x.call, x.finishers, x.hidden
                    })
                    .Where(x => x.anyOf != null || x.chapter || x.autoStart || x.autoFinish || x.dialogOnly || x.icon != null || x.notes != null || x.items.Count > 0 || x.startAfter != null || x.order != null || x.noteLinks != null || x.story || x.noCounter != null || x.unlockDialogue.Count > 0 || x.talkTo != null || x.unlockLocations.Count > 0 || x.call != null || x.finishers.Count > 0 || x.hidden)
                    .ToDictionary(x => x.id, x => new { x.anyOf, x.chapter, x.autoStart, x.autoFinish, x.dialogOnly, x.icon, x.notes, x.items, x.startAfter, x.order, x.noteLinks, x.story, x.noCounter, x.unlockDialogue, x.talkTo, x.unlockLocations, x.call, x.hidden, finishers = x.finishers.Count > 0 ? x.finishers : null, subs = x.chapter ? x.subs : null })),
            typeof(QuestReadyRequest))
    ])
{
    static JsonElement? Visit(Dictionary<string, object> ext) =>
        ext != null && ext.TryGetValue("visitapi", out var v) && v is JsonElement e && e.ValueKind == JsonValueKind.Object ? e : null;

    static bool Flag(JsonElement? vx, string name) => vx?.TryGetProperty(name, out var p) == true && p.ValueKind == JsonValueKind.True;

    static object AnyOf(JsonElement? vx) => Flag(vx, "anyOf") ? (object)true
        : vx?.TryGetProperty("anyOf", out var p) == true && p.ValueKind == JsonValueKind.Array ? StrList(vx, "anyOf") : null;

    static bool Story(Dictionary<string, object> ext) =>
        ext != null && ext.TryGetValue("isStoryQuest", out var v) && v is JsonElement e && e.ValueKind == JsonValueKind.True;

    static bool Hidden(Dictionary<string, object> ext) =>
        ext != null && ext.TryGetValue("notDisplayedQuest", out var v) && v is JsonElement e && e.ValueKind == JsonValueKind.True;

    static string Str(JsonElement? vx, string name) => vx?.TryGetProperty(name, out var p) == true && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    static double? Num(JsonElement? vx, string name) => vx?.TryGetProperty(name, out var p) == true && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : null;

    static Dictionary<string, bool> Subs(Quest q)
    {
        var result = new Dictionary<string, bool>();
        foreach (var c in q.Conditions?.AvailableForFinish ?? new List<QuestCondition>())
        {
            if (c.ConditionType != "Quest" || c.Target == null) continue;
            var targets = c.Target.IsItem ? new List<string> { c.Target.Item } : c.Target.List ?? new List<string>();
            foreach (var target in targets)
                if (!string.IsNullOrEmpty(target)) result[target] = c.IsNecessary ?? true;
        }
        return result;
    }

    static List<string> Finishers(Quest q)
    {
        var list = new List<string>();
        foreach (var c in q.Conditions?.AvailableForFinish ?? new List<QuestCondition>())
        {
            if (c.ConditionType != "Quest" || c.Target == null || c.ExtensionData == null) continue;
            if (!(c.ExtensionData.TryGetValue("isFinisher", out var v) && v is JsonElement e && e.ValueKind == JsonValueKind.True)) continue;
            foreach (var t in c.Target.IsItem ? new List<string> { c.Target.Item } : c.Target.List ?? new List<string>())
                if (!string.IsNullOrEmpty(t)) list.Add(t);
        }
        return list;
    }

    static List<string> Items(JsonElement? vx) => StrList(vx, "items");

    static List<string> StrList(JsonElement? vx, string name) =>
        vx?.TryGetProperty(name, out var p) == true && p.ValueKind == JsonValueKind.Array
            ? p.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()).ToList() : new List<string>();

    static Dictionary<string, string> _dialogTrader;
    static string TalkTo(Quest q, TemplateTable templates)
    {
        var dialogueId = q.DialogueId?.ToString();
        if (string.IsNullOrEmpty(dialogueId)) return null;
        var map = _dialogTrader;
        if (map == null)
        {
            map = new Dictionary<string, string>();
            foreach (var el in templates.Dialogue?.Elements ?? new List<TraderDialogElement>()) map[el.Id.ToString()] = el.MainTrader.ToString();
            _dialogTrader = map;
        }
        return map.TryGetValue(dialogueId, out var trader) && trader.Length == 24 ? trader : null;
    }

    static string Call(Dictionary<string, object> ext)
    {
        if (ext == null || !ext.TryGetValue("mailSettings", out var v) || v is not JsonElement e || e.ValueKind != JsonValueKind.Object) return null;
        if (!(e.TryGetProperty("isEnabled", out var on) && on.ValueKind == JsonValueKind.True)) return null;
        var trader = Str(e, "dialogueTraderId") ?? Str(e, "fromTraderId");
        return trader?.Length == 24 ? trader : null;
    }

    static List<string> NoCounter(Quest q)
    {
        List<string> ids = null;
        foreach (var c in q.Conditions?.AvailableForFinish ?? new List<QuestCondition>())
            if (c.ExtensionData != null && c.ExtensionData.TryGetValue("showCounter", out var v) && v is JsonElement e && e.ValueKind == JsonValueKind.False)
                (ids ??= new List<string>()).Add(c.Id.ToString());
        return ids;
    }

    static JsonElement? Obj(JsonElement? vx, string name) => vx?.TryGetProperty(name, out var p) == true && p.ValueKind == JsonValueKind.Object ? p : null;

    static Dictionary<string, string> Notes(Quest q)
    {
        var ext = q.ExtensionData;
        var notes = ext != null && ext.TryGetValue("notes", out var v) && v is JsonElement e && e.ValueKind == JsonValueKind.Object
            ? e.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String).ToDictionary(p => p.Name, p => p.Value.GetString()) : null;
        foreach (var c in q.Conditions?.AvailableForFinish ?? new List<QuestCondition>())
        {
            if (c.ExtensionData == null || !c.ExtensionData.TryGetValue("questNoteId", out var n) || n is not JsonElement ne || ne.ValueKind != JsonValueKind.String) continue;
            var noteId = ne.GetString();
            if (string.IsNullOrEmpty(noteId)) continue;
            (notes ??= new Dictionary<string, string>())["cond:" + c.Id] = noteId;
        }
        return notes;
    }
}
