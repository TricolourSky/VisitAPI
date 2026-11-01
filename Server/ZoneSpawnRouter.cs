using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Utils;

namespace VisitAPI.Server;

/// <summary>包里 zones\*.json（交互区）和 spawns\*.json（剧情刷兵点，10-01 SORA 拍板：一条 = 地图 + 圆心/半径 + 兵种 + 数量 + 任务门）的数组原样拼给客户端。
/// 服务端只做搬运工，判定和落点全在客户端（它才知道任务状态和导航网格）。第一次请求时读，之后用缓存。</summary>
[Injectable]
public class ZoneSpawnRouter(JsonUtil jsonUtil, HttpResponseUtil httpResponse, ISptLogger<ZoneSpawnRouter> log)
    : StaticRouter(jsonUtil, [
        new RouteAction("/visitapi/zones",
            async (url, info, sessionId, output, ct) => httpResponse.GetBody(LoadArray("zones", dedupeById: true, log)),
            typeof(EmptyRequestData)),
        new RouteAction("/visitapi/spawns",
            async (url, info, sessionId, output, ct) => httpResponse.GetBody(LoadArray("spawns", dedupeById: false, log)),
            typeof(EmptyRequestData))
    ])
{
    static readonly ConcurrentDictionary<string, List<JsonElement>> Cache = new();

    /// dedupeById（zones）：区域 id → 包/文件，不同文件撞了点名、先来的赢。同一个文件里的同名条目是有意的「一个区域多个框」
    ///（09-25 迷宫入口：1.1 的通道 + 0.16 的转移点），客户端每条各生成一个触发器、触发的是同一个 id，照常放行
    static List<JsonElement> LoadArray(string kind, bool dedupeById, ISptLogger<ZoneSpawnRouter> log)
    {
        if (Cache.TryGetValue(kind, out var cached)) return cached;
        var list = new List<JsonElement>();
        var owner = new Dictionary<string, string>(StringComparer.Ordinal);
        PackJson.Read(ContentPacks.All(log), kind, (label, root) =>
        {
            if (root.ValueKind != JsonValueKind.Array) { log.Error($"[VisitAPI] {kind} {label}: top level is not an array, skipped"); return; }
            foreach (var z in root.EnumerateArray())
            {
                var id = dedupeById && z.ValueKind == JsonValueKind.Object && z.TryGetProperty("id", out var idv) && idv.ValueKind == JsonValueKind.String ? idv.GetString() : null;
                if (id != null && owner.TryGetValue(id, out var first) && first != label) { log.Error($"[VisitAPI] Zone {id} exists in both {first} and {label}, using the former"); continue; }
                if (id != null) owner[id] = label;
                list.Add(z.Clone());
            }
        }, (label, e) => log.Error($"[VisitAPI] {kind} {label} could not be read: {e.Message}"));
        Cache[kind] = list;
        return list;
    }
}
