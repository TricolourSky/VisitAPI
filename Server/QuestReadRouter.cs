using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
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
                    await ProfileLocks.RunAsync(sessionId, ct, async () =>
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
                    await ProfileLocks.RunAsync(sessionId, ct, async () =>
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
    const string Key = "visitapi";

    class State
    {
        public HashSet<string> Read = new(StringComparer.Ordinal);
        public HashSet<string> Done = new(StringComparer.Ordinal);
        public HashSet<string> Skipped = new(StringComparer.Ordinal);
        public Dictionary<string, int> Counts = new(StringComparer.Ordinal);
    }

    /// 档案里的 visitapi 永远是 JsonElement：从磁盘读进来是，本次运行里 Store 写的也是（10-17 审查 E21）
    static State Load(SptProfile profile)
    {
        var s = new State();
        if (Ext.Get(profile.ExtensionData, Key, JsonValueKind.Object) is not { } je) return s;
        if (je.TryGetProperty("read", out var r)) Strings(r, s.Read);
        if (je.TryGetProperty("objectives", out var o) && o.ValueKind == JsonValueKind.Object)
        {
            if (o.TryGetProperty("done", out var d)) Strings(d, s.Done);
            if (o.TryGetProperty("skipped", out var k)) Strings(k, s.Skipped);
            if (o.TryGetProperty("counts", out var c) && c.ValueKind == JsonValueKind.Object)
                foreach (var p in c.EnumerateObject()) if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var n)) s.Counts[p.Name] = n;
        }
        return s;
    }

    static void Strings(JsonElement arr, HashSet<string> into)
    {
        if (arr.ValueKind != JsonValueKind.Array) return;
        foreach (var e in arr.EnumerateArray()) if (e.ValueKind == JsonValueKind.String) into.Add(e.GetString());
    }

    /// 存成和直接存 Dictionary 一样的 JSON 对象（存盘时 SPT 把 JsonElement 原样写出）
    static void Store(SptProfile profile, State s)
    {
        profile.ExtensionData ??= new Dictionary<string, object>();
        profile.ExtensionData[Key] = JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["read"] = s.Read.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            ["objectives"] = new Dictionary<string, object>
            {
                ["done"] = s.Done.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                ["skipped"] = s.Skipped.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                ["counts"] = new Dictionary<string, int>(s.Counts, StringComparer.Ordinal)
            }
        });
    }
}
