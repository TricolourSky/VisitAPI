using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VisitAPI;

// Phase 1 author-layer salvage: per-trader dialog state at <config>/VisitAPI/<traderId>.seen.json, keyed by
// profileId so each save tracks its own first-meeting + one-time ("永久已读") option choices. Save is
// hand-rolled (StringBuilder + JsonConvert.ToString) so a mod tampering with Newtonsoft's global
// DefaultSettings can't break it (DEV_NOTES #1). Cached per trader, one disk read each (DEV_NOTES #9).
internal static class DialogStateStore
{
    private static string ConfigDir => DialogTreeLoader.BaseDir;
    private const string VisitedKey = "__visited__";

    private static readonly Dictionary<string, Dictionary<string, HashSet<string>>> _cache =
        new Dictionary<string, Dictionary<string, HashSet<string>>>(StringComparer.OrdinalIgnoreCase);

    private static string FilePath(string traderId) => Path.Combine(ConfigDir, traderId + ".seen.json");

    private static Dictionary<string, HashSet<string>> Load(string traderId)
    {
        if (_cache.TryGetValue(traderId, out Dictionary<string, HashSet<string>> cached)) return cached;
        Dictionary<string, HashSet<string>> data = new Dictionary<string, HashSet<string>>();
        _cache[traderId] = data;
        string path = FilePath(traderId);
        if (!File.Exists(path)) return data;
        try
        {
            JObject jobj = JObject.Parse(File.ReadAllText(path));
            foreach (var kv in jobj)
            {
                HashSet<string> set = new HashSet<string>();
                if (kv.Value is JArray arr)
                    foreach (var item in arr) set.Add(item.Value<string>() ?? "");
                else if (kv.Value?.Type == JTokenType.String)
                    set.Add(kv.Value.Value<string>() ?? "");
                if (set.Count > 0) data[kv.Key] = set;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning("[DialogStateStore] load '" + path + "': " + ex.Message);
        }
        return data;
    }

    internal static bool IsFirstVisit(string traderId, string profileId)
    {
        if (string.IsNullOrEmpty(traderId)) return false;
        Dictionary<string, HashSet<string>> data = Load(traderId);
        if (data.TryGetValue(profileId, out HashSet<string> own) && own.Contains(VisitedKey)) return false;
        if (!string.IsNullOrEmpty(profileId) && data.TryGetValue("", out HashSet<string> global) && global.Contains(VisitedKey)) return false;
        return true;
    }

    internal static void MarkVisited(string traderId, string profileId) => Add(traderId, profileId, VisitedKey);

    internal static bool IsSeen(string traderId, string profileId, string nodeId, int optionIndex)
    {
        if (string.IsNullOrEmpty(traderId)) return false;
        return Load(traderId).TryGetValue(profileId, out HashSet<string> set) && set.Contains(nodeId + "/" + optionIndex);
    }

    internal static void MarkSeen(string traderId, string profileId, string nodeId, int optionIndex)
        => Add(traderId, profileId, nodeId + "/" + optionIndex);

    private static void Add(string traderId, string profileId, string key)
    {
        if (string.IsNullOrEmpty(traderId)) return;
        Dictionary<string, HashSet<string>> data = Load(traderId);
        if (!data.TryGetValue(profileId, out HashSet<string> set))
            set = data[profileId] = new HashSet<string>();
        if (set.Add(key)) Save(traderId, data);
    }

    private static void Save(string traderId, Dictionary<string, HashSet<string>> data)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            StringBuilder sb = new StringBuilder();
            sb.Append("{");
            bool firstKey = true;
            foreach (KeyValuePair<string, HashSet<string>> item in data)
            {
                sb.Append(firstKey ? "\n" : ",\n");
                firstKey = false;
                sb.Append("  ").Append(JsonConvert.ToString(item.Key)).Append(": [");
                bool firstValue = true;
                foreach (string value in item.Value)
                {
                    if (!firstValue) sb.Append(", ");
                    firstValue = false;
                    sb.Append(JsonConvert.ToString(value));
                }
                sb.Append("]");
            }
            sb.Append("\n}");
            File.WriteAllText(FilePath(traderId), sb.ToString());
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning("[DialogStateStore] save: " + ex.Message);
        }
    }
}
