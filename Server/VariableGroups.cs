using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using SPTarkov.Server.Core.Models.Common;
using VisitAPI.Packs;

namespace VisitAPI.Server;

public static class VariableGroups
{
    static readonly object Gate = new();
    static List<(MongoId id, List<MongoId> members)> _groups;

    /// <summary>各包 variables\*.json（1.1 原表 groups.json 的形状：[{id, variables[]}]）合起来；同 id 先来的赢。文件读不动就整个跳过（和以前一样不吭声）。</summary>
    static List<(MongoId id, List<MongoId> members)> Load()
    {
        lock (Gate)
        {
            if (_groups != null) return _groups;
            var list = new List<(MongoId, List<MongoId>)>();
            var seen = new HashSet<string>();
            foreach (var p in ContentPacks.All())
                foreach (var file in PackLayout.DataFiles(p, "variables"))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllBytes(file), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                        if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;
                        foreach (var g in doc.RootElement.EnumerateArray())
                        {
                            if (!g.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String || id.GetString()?.Length != 24 || !seen.Add(id.GetString())) continue;
                            var members = new List<MongoId>();
                            if (g.TryGetProperty("variables", out var vs) && vs.ValueKind == JsonValueKind.Array)
                                foreach (var v in vs.EnumerateArray()) if (v.ValueKind == JsonValueKind.String && v.GetString()?.Length == 24) members.Add(new MongoId(v.GetString()));
                            if (members.Count > 0) list.Add((new MongoId(id.GetString()), members));
                        }
                    }
                    catch (Exception) {  }
                }
            return _groups = list;
        }
    }

    public static object Payload() => Load().Select(g => new { id = g.id.ToString(), variables = g.members.Select(m => m.ToString()).ToList() }).ToList();

    public static int Recompute(Dictionary<MongoId, int> vars)
    {
        if (vars == null) return 0;
        var changed = 0;
        foreach (var (id, members) in Load())
        {
            var sum = members.Sum(m => vars.TryGetValue(m, out var v) ? v : 0);
            if (vars.TryGetValue(id, out var old) && old == sum) continue;
            vars[id] = sum;
            changed++;
        }
        return changed;
    }
}
