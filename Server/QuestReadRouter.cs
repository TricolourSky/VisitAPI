using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
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

/// <summary>章节页的"已读"状态（G4/B1）：跟档案存，换档不串。存在 SptProfile 的 JsonExtensionData 顶层键
/// `visitapi: { read: [...] }` 里——SPT 对未知键原样进出档案 JSON，服务端升级/换机都带着走。
/// 客户端登录拉一次全量（/read/list），之后只增量上报（/read）；每次写完立刻落盘，游戏崩了也不丢。</summary>
[Injectable]
public class QuestReadRouter(JsonUtil jsonUtil, ProfileHelper profiles, SaveServer saveServer, HttpResponseUtil httpResponse)
    : StaticRouter(jsonUtil, [
        new RouteAction("/visitapi/quest/read",
            async (url, info, sessionId, output, ct) =>
            {
                var ids = ((QuestReadRequest)info).Ids;
                var profile = profiles.GetFullProfile(sessionId);
                if (profile != null && ids != null && ids.Count > 0)
                {
                    // 09-07 终审：这是一段读-改-写，客户端悬停上报与 4 秒全读会并发到达；不串行化的话后到的一笔会把先到的覆盖掉
                    //（已读丢失、下次登录绿标复现）。按档案加锁，锁只包这一段。
                    var gate = Locks.GetOrAdd(sessionId.ToString(), _ => new SemaphoreSlim(1, 1));
                    await gate.WaitAsync(ct);
                    try
                    {
                        var set = ReadSet(profile);
                        var changed = false;
                        foreach (var id in ids) if (!string.IsNullOrEmpty(id)) changed |= set.Add(id);
                        if (changed)
                        {
                            Store(profile, set);
                            await saveServer.SaveProfileAsync(sessionId, ct);
                        }
                    }
                    finally { gate.Release(); }
                }
                return httpResponse.EmptyResponse();
            },
            typeof(QuestReadRequest)),
        new RouteAction("/visitapi/quest/read/list",
            async (url, info, sessionId, output, ct) =>
            {
                var profile = profiles.GetFullProfile(sessionId);
                return httpResponse.GetBody(profile == null ? new List<string>() : ReadSet(profile).OrderBy(x => x, StringComparer.Ordinal).ToList());
            },
            typeof(QuestReadRequest))
    ])
{
    static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    /// 档案里的已读集合。两种形态都认：刚从磁盘载入是 JsonElement，本进程写过是 Dictionary/List
    static HashSet<string> ReadSet(SptProfile profile)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        var ext = profile.ExtensionData;
        if (ext != null && ext.TryGetValue("visitapi", out var v))
        {
            if (v is JsonElement je && je.ValueKind == JsonValueKind.Object && je.TryGetProperty("read", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var e in arr.EnumerateArray()) { if (e.ValueKind == JsonValueKind.String) set.Add(e.GetString()); }
            else if (v is Dictionary<string, object> d && d.TryGetValue("read", out var l) && l is List<string> list)
                foreach (var s in list) set.Add(s);
        }
        return set;
    }

    static void Store(SptProfile profile, HashSet<string> set)
    {
        profile.ExtensionData ??= new Dictionary<string, object>();
        profile.ExtensionData["visitapi"] = new Dictionary<string, object> { ["read"] = set.OrderBy(x => x, StringComparer.Ordinal).ToList() };
    }
}
