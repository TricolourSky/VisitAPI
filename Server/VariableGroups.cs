using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SPTarkov.Server.Core.Models.Common;

namespace VisitAPI.Server;

public static class VariableGroups
{
    static readonly object Gate = new();
    static List<(MongoId id, List<MongoId> members)> _groups;

    /// <summary>各包 variables\*.json（1.1 原表 groups.json 的形状：[{id, variables[]}]）合起来；同 id 先来的赢。文件读不动就整个跳过（不吭声）。</summary>
    static List<(MongoId id, List<MongoId> members)> Load()
    {
        lock (Gate)
        {
            if (_groups != null) return _groups;
            var list = new List<(MongoId, List<MongoId>)>();
            var seen = new HashSet<string>();
            PackJson.Read(ContentPacks.All(), "variables", (_, root) =>
            {
                if (root.ValueKind != JsonValueKind.Array) return;
                foreach (var g in root.EnumerateArray())
                {
                    if (!g.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String || id.GetString()?.Length != 24 || !seen.Add(id.GetString())) continue;
                    var members = new List<MongoId>();
                    if (g.TryGetProperty("variables", out var vs) && vs.ValueKind == JsonValueKind.Array)
                        foreach (var v in vs.EnumerateArray()) if (v.ValueKind == JsonValueKind.String && v.GetString()?.Length == 24) members.Add(new MongoId(v.GetString()));
                    if (members.Count > 0) list.Add((new MongoId(id.GetString()), members));
                }
            }, null);
            return _groups = list;
        }
    }

    public static object Payload() => Load().Select(g => new { id = g.id.ToString(), variables = g.members.Select(m => m.ToString()).ToList() }).ToList();

    public static void Recompute(Dictionary<MongoId, int> vars)
    {
        if (vars == null) return;
        foreach (var (id, members) in Load())
        {
            var sum = members.Sum(m => vars.TryGetValue(m, out var v) ? v : 0);
            if (vars.TryGetValue(id, out var old) && old == sum) continue;
            vars[id] = sum;
        }
    }
}
