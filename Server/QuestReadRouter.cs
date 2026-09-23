using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;

namespace VisitAPI.Server;

public class QuestReadRequest : IRequestData
{
    [JsonPropertyName("ids")] public List<string> Ids { get; set; }
}

public class ObjectivesRequest : IRequestData
{
    [JsonPropertyName("done")] public List<string> Done { get; set; }
    [JsonPropertyName("skipped")] public List<string> Skipped { get; set; }
    [JsonPropertyName("counts")] public Dictionary<string, int> Counts { get; set; }
}

[Injectable]
public class QuestReadRouter(JsonUtil jsonUtil, ProfileHelper profiles, SaveServer saveServer, HttpResponseUtil httpResponse)
    : StaticRouter(jsonUtil, [
        new RouteAction("/visitapi/quest/read",
            async (url, info, sessionId, output, ct) =>
            {
                var ids = ((QuestReadRequest)info).Ids;
                var profile = profiles.GetFullProfile(sessionId);
                if (profile != null && ids != null && ids.Count > 0)
                    await Locked(sessionId, ct, async () =>
                    {
                        var state = Load(profile);
                        var changed = false;
                        foreach (var id in ids) if (!string.IsNullOrEmpty(id)) changed |= state.Read.Add(id);
                        if (changed) { Store(profile, state); await saveServer.SaveProfileAsync(sessionId, ct); }
                    });
                return httpResponse.EmptyResponse();
            },
            typeof(QuestReadRequest)),
        new RouteAction("/visitapi/quest/read/list",
            async (url, info, sessionId, output, ct) =>
            {
                var profile = profiles.GetFullProfile(sessionId);
                return httpResponse.GetBody(profile == null ? new List<string>() : Load(profile).Read.OrderBy(x => x, StringComparer.Ordinal).ToList());
            },
            typeof(QuestReadRequest)),
        new RouteAction("/visitapi/quest/objectives",
            async (url, info, sessionId, output, ct) =>
            {
                var req = (ObjectivesRequest)info;
                var profile = profiles.GetFullProfile(sessionId);
                if (profile != null && req != null)
                    await Locked(sessionId, ct, async () =>
                    {
                        var state = Load(profile);
                        var changed = false;
                        foreach (var id in req.Done ?? new List<string>()) if (!string.IsNullOrEmpty(id)) { changed |= state.Done.Add(id); changed |= state.Skipped.Remove(id); }
                        foreach (var id in req.Skipped ?? new List<string>()) if (!string.IsNullOrEmpty(id) && !state.Done.Contains(id)) changed |= state.Skipped.Add(id);
                        foreach (var (id, n) in req.Counts ?? new Dictionary<string, int>()) if (!string.IsNullOrEmpty(id) && (!state.Counts.TryGetValue(id, out var old) || old != n)) { state.Counts[id] = n; changed = true; }
                        if (changed) { Store(profile, state); await saveServer.SaveProfileAsync(sessionId, ct); }
                    });
                return httpResponse.EmptyResponse();
            },
            typeof(ObjectivesRequest)),
        new RouteAction("/visitapi/quest/objectives/list",
            async (url, info, sessionId, output, ct) =>
            {
                var profile = profiles.GetFullProfile(sessionId);
                var state = profile == null ? new State() : Load(profile);
                return httpResponse.GetBody(new
                {
                    done = state.Done.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                    skipped = state.Skipped.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                    counts = state.Counts
                });
            },
            typeof(ObjectivesRequest))
    ])
{
    static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    class State
    {
        public HashSet<string> Read = new(StringComparer.Ordinal);
        public HashSet<string> Done = new(StringComparer.Ordinal);
        public HashSet<string> Skipped = new(StringComparer.Ordinal);
        public Dictionary<string, int> Counts = new(StringComparer.Ordinal);
    }

    static async Task Locked(string sessionId, CancellationToken ct, Func<Task> body)
    {
        var gate = Locks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try { await body(); }
        finally { gate.Release(); }
    }

    static State Load(SptProfile profile)
    {
        var s = new State();
        var ext = profile.ExtensionData;
        if (ext == null || !ext.TryGetValue("visitapi", out var v)) return s;
        if (v is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            if (je.TryGetProperty("read", out var r)) Strings(r, s.Read);
            if (je.TryGetProperty("objectives", out var o) && o.ValueKind == JsonValueKind.Object)
            {
                if (o.TryGetProperty("done", out var d)) Strings(d, s.Done);
                if (o.TryGetProperty("skipped", out var k)) Strings(k, s.Skipped);
                if (o.TryGetProperty("counts", out var c) && c.ValueKind == JsonValueKind.Object)
                    foreach (var p in c.EnumerateObject()) if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var n)) s.Counts[p.Name] = n;
            }
        }
        else if (v is Dictionary<string, object> dict)
        {
            if (dict.TryGetValue("read", out var r) && r is List<string> rl) foreach (var x in rl) s.Read.Add(x);
            if (dict.TryGetValue("objectives", out var o) && o is Dictionary<string, object> od)
            {
                if (od.TryGetValue("done", out var d) && d is List<string> dl) foreach (var x in dl) s.Done.Add(x);
                if (od.TryGetValue("skipped", out var k) && k is List<string> kl) foreach (var x in kl) s.Skipped.Add(x);
                if (od.TryGetValue("counts", out var c) && c is Dictionary<string, int> cd) foreach (var (id, n) in cd) s.Counts[id] = n;
            }
        }
        return s;
    }

    static void Strings(JsonElement arr, HashSet<string> into)
    {
        if (arr.ValueKind != JsonValueKind.Array) return;
        foreach (var e in arr.EnumerateArray()) if (e.ValueKind == JsonValueKind.String) into.Add(e.GetString());
    }

    static void Store(SptProfile profile, State s)
    {
        profile.ExtensionData ??= new Dictionary<string, object>();
        profile.ExtensionData["visitapi"] = new Dictionary<string, object>
        {
            ["read"] = s.Read.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            ["objectives"] = new Dictionary<string, object>
            {
                ["done"] = s.Done.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                ["skipped"] = s.Skipped.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                ["counts"] = new Dictionary<string, int>(s.Counts, StringComparer.Ordinal)
            }
        };
    }
}
