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
        var owner = new Dictionary<string, string>(StringComparer.Ordinal);   // 区域 id → 包/文件：撞了点名、先来的赢（以前不查，客户端也不查）
        foreach (var p in ContentPacks.All(log))
            foreach (var file in PackLayout.DataFiles(p, "zones"))
            {
                var label = p.Name + "/" + Path.GetFileName(file);
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllBytes(file), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                    if (doc.RootElement.ValueKind != JsonValueKind.Array) { log.Error($"[VisitAPI] zones {label}: 顶层不是数组，跳过"); continue; }
                    foreach (var z in doc.RootElement.EnumerateArray())
                    {
                        var id = z.ValueKind == JsonValueKind.Object && z.TryGetProperty("id", out var idv) && idv.ValueKind == JsonValueKind.String ? idv.GetString() : null;
                        if (id != null && owner.TryGetValue(id, out var first)) { log.Error($"[VisitAPI] 区域 {id} 在 {first} 和 {label} 里都有，用了前者的"); continue; }
                        if (id != null) owner[id] = label;
                        list.Add(z.Clone());
                    }
                }
                catch (Exception e) { log.Error($"[VisitAPI] zones {label} 读不动: {e.Message}"); }
            }
        _zones = list;
        return list;
    }
}
