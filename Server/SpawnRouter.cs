using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;
using VisitAPI.Packs;
using Path = System.IO.Path;

namespace VisitAPI.Server;

public class SpawnRequest : IRequestData { }

/// <summary>剧情刷兵点（10-01 SORA 拍板）：包的 spawns\*.json 原样拼给客户端，一条 = 地图 + 圆心/半径 + 兵种 + 数量 + 任务门。
/// 和 zones 一个路子：服务端只做搬运工，判定和落点全在客户端（它才知道任务状态和导航网格）。</summary>
[Injectable]
public class SpawnRouter(JsonUtil jsonUtil, HttpResponseUtil httpResponse, ISptLogger<SpawnRouter> log)
    : StaticRouter(jsonUtil, [
        new RouteAction("/visitapi/spawns",
            async (url, info, sessionId, output, ct) => httpResponse.GetBody(Load(log)),
            typeof(SpawnRequest))
    ])
{
    static List<JsonElement> _spawns;

    static List<JsonElement> Load(ISptLogger<SpawnRouter> log)
    {
        if (_spawns != null) return _spawns;
        var list = new List<JsonElement>();
        foreach (var p in ContentPacks.All(log))
            foreach (var file in PackLayout.DataFiles(p, "spawns"))
            {
                var label = p.Name + "/" + Path.GetFileName(file);
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllBytes(file), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                    if (doc.RootElement.ValueKind != JsonValueKind.Array) { log.Error($"[VisitAPI] spawns {label}: top level is not an array, skipped"); continue; }
                    foreach (var s in doc.RootElement.EnumerateArray()) list.Add(s.Clone());
                }
                catch (Exception e) { log.Error($"[VisitAPI] spawns {label} could not be read: {e.Message}"); }
            }
        _spawns = list;
        return list;
    }
}
