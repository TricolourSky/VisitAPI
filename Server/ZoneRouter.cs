using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;
using VisitAPI.Packs;
using Path = System.IO.Path;

namespace VisitAPI.Server;

public class ZoneRequest : IRequestData { }

[Injectable]
public class ZoneRouter(JsonUtil jsonUtil, HttpResponseUtil httpResponse, ISptLogger<ZoneRouter> log)
    : StaticRouter(jsonUtil, [
        new RouteAction("/visitapi/zones",
            async (url, info, sessionId, output, ct) => httpResponse.GetBody(Load(log)),
            typeof(ZoneRequest))
    ])
{
    static List<JsonElement> _zones;

    static List<JsonElement> Load(ISptLogger<ZoneRouter> log)
    {
        if (_zones != null) return _zones;
        var list = new List<JsonElement>();
        // 区域 id → 包/文件：不同文件撞了点名、先来的赢。同一个文件里的同名条目是有意的「一个区域多个框」（09-25 迷宫入口：1.1 的通道 + 0.16 的转移点），
        // 客户端每条各生成一个触发器、触发的是同一个 id，照常放行
        var owner = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in ContentPacks.All(log))
            foreach (var file in PackLayout.DataFiles(p, "zones"))
            {
                var label = p.Name + "/" + Path.GetFileName(file);
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllBytes(file), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                    if (doc.RootElement.ValueKind != JsonValueKind.Array) { log.Error($"[VisitAPI] zones {label}: top level is not an array, skipped"); continue; }
                    foreach (var z in doc.RootElement.EnumerateArray())
                    {
                        var id = z.ValueKind == JsonValueKind.Object && z.TryGetProperty("id", out var idv) && idv.ValueKind == JsonValueKind.String ? idv.GetString() : null;
                        if (id != null && owner.TryGetValue(id, out var first) && first != label) { log.Error($"[VisitAPI] Zone {id} exists in both {first} and {label}, using the former"); continue; }
                        if (id != null) owner[id] = label;
                        list.Add(z.Clone());
                    }
                }
                catch (Exception e) { log.Error($"[VisitAPI] zones {label} could not be read: {e.Message}"); }
            }
        _zones = list;
        return list;
    }
}
