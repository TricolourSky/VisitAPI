using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VisitAPI;

internal static class DialogStateStore
{
	private static readonly string ConfigDir = Path.Combine(BepInEx.Paths.ConfigPath, "VisitAPI");

	// 按 traderId 缓存，每个商人的 .seen.json 只读盘一次。
	// 旧实现只缓存"最后一个 trader"，多个触发器并存时每帧互相挤掉缓存，导致反复读盘解析（卡顿源）。
	private static readonly Dictionary<string, Dictionary<string, HashSet<string>>> s_cache =
		new Dictionary<string, Dictionary<string, HashSet<string>>>(StringComparer.OrdinalIgnoreCase);

	private static string FilePath(string traderId)
	{
		return Path.Combine(ConfigDir, traderId + ".seen.json");
	}

	private static Dictionary<string, HashSet<string>> Load(string traderId)
	{
		if (s_cache.TryGetValue(traderId, out Dictionary<string, HashSet<string>> cached))
		{
			return cached;
		}
		Dictionary<string, HashSet<string>> data = new Dictionary<string, HashSet<string>>();
		s_cache[traderId] = data;
		string text = FilePath(traderId);
		if (!File.Exists(text))
		{
			return data;
		}
		try
		{
			JObject jobj = JObject.Parse(File.ReadAllText(text));
			foreach (var kv in jobj)
			{
				var set = new HashSet<string>();
				if (kv.Value is JArray arr)
					foreach (var item in arr) set.Add(item.Value<string>() ?? "");
				else if (kv.Value?.Type == JTokenType.String)
					set.Add(kv.Value.Value<string>() ?? "");
				if (set.Count > 0)
					data[kv.Key] = set;
			}
		}
		catch (Exception ex)
		{
			VisitPlugin.Log.LogWarning((object)("DialogStateStore: load '" + text + "': " + ex.Message));
		}
		return data;
	}

	public static bool IsFirstVisit(string traderId, string profileId)
	{
		Dictionary<string, HashSet<string>> data = Load(traderId);
		if (data.TryGetValue(profileId, out HashSet<string> value) && value.Contains("__visited__"))
		{
			return false;
		}
		if (!string.IsNullOrEmpty(profileId) && data.TryGetValue("", out HashSet<string> value2) && value2.Contains("__visited__"))
		{
			return false;
		}
		return true;
	}

	public static void MarkVisited(string traderId, string profileId)
	{
		Dictionary<string, HashSet<string>> data = Load(traderId);
		if (!data.TryGetValue(profileId, out HashSet<string> value))
		{
			value = (data[profileId] = new HashSet<string>());
		}
		if (value.Add("__visited__"))
		{
			Save(traderId, data);
		}
	}

	public static bool IsSeen(string traderId, string profileId, string nodeId, int optionIndex)
	{
		if (Load(traderId).TryGetValue(profileId, out HashSet<string> value))
		{
			return value.Contains($"{nodeId}/{optionIndex}");
		}
		return false;
	}

	public static void MarkSeen(string traderId, string profileId, string nodeId, int optionIndex)
	{
		Dictionary<string, HashSet<string>> data = Load(traderId);
		if (!data.TryGetValue(profileId, out HashSet<string> value))
		{
			value = (data[profileId] = new HashSet<string>());
		}
		if (value.Add($"{nodeId}/{optionIndex}"))
		{
			Save(traderId, data);
		}
	}

	private static void Save(string traderId, Dictionary<string, HashSet<string>> data)
	{
		try
		{
			Directory.CreateDirectory(ConfigDir);
			// 手写序列化：不走 JsonConvert.SerializeObject，避免被其他 mod 篡改的
			// Newtonsoft 全局 DefaultSettings 影响（曾出现 "Self referencing loop" 误报导致保存失败）
			System.Text.StringBuilder sb = new System.Text.StringBuilder();
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
					if (!firstValue)
					{
						sb.Append(", ");
					}
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
			VisitPlugin.Log.LogWarning((object)("DialogStateStore: save: " + ex.Message));
		}
	}
}

// ── Dialog tree loader ─────────────────────────────────────────────────────────

internal static class DialogTreeLoader
{
	private static readonly string ConfigDir = Path.Combine(BepInEx.Paths.ConfigPath, "VisitAPI");

	public static bool IsRegistered(string? traderId)
	{
		if (string.IsNullOrEmpty(traderId)) return false;
		return File.Exists(Path.Combine(ConfigDir, traderId + ".dlg"))
			|| File.Exists(Path.Combine(ConfigDir, traderId + ".json"));
	}

	/// <summary>枚举已注册对话的商人 ID（.dlg 与 .json 并集，排除状态/配置文件）。</summary>
	public static IEnumerable<string> EnumerateTraderIds()
	{
		if (!Directory.Exists(ConfigDir)) yield break;
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string file in Directory.GetFiles(ConfigDir, "*.dlg"))
		{
			string name = Path.GetFileNameWithoutExtension(file);
			if (seen.Add(name)) yield return name;
		}
		foreach (string file in Directory.GetFiles(ConfigDir, "*.json"))
		{
			string name = Path.GetFileNameWithoutExtension(file);
			if (name.EndsWith(".seen", StringComparison.OrdinalIgnoreCase)) continue;
			if (string.Equals(name, "hideout_triggers", StringComparison.OrdinalIgnoreCase)) continue;
			if (string.Equals(name, "pending_random", StringComparison.OrdinalIgnoreCase)) continue;
			if (seen.Add(name)) yield return name;
		}
	}

	private sealed class CachedTree
	{
		public DialogTree? Tree;
		public string Path = "";
		public DateTime LastWrite;
	}

	// 解析结果按商人缓存，文件时间戳变化时自动失效（支持游戏运行中热改对话文件）
	private static readonly Dictionary<string, CachedTree> s_treeCache =
		new Dictionary<string, CachedTree>(StringComparer.OrdinalIgnoreCase);

	public static DialogTree? TryLoad(string traderId)
	{
		string dlgPath = Path.Combine(ConfigDir, traderId + ".dlg");
		string primary = File.Exists(dlgPath) ? dlgPath : Path.Combine(ConfigDir, traderId + ".json");
		if (!File.Exists(primary)) return null;
		DateTime stamp = File.GetLastWriteTimeUtc(primary);
		if (s_treeCache.TryGetValue(traderId, out CachedTree cached) && cached.Path == primary && cached.LastWrite == stamp)
		{
			return cached.Tree;
		}
		DialogTree? tree = LoadUncached(traderId, primary);
		s_treeCache[traderId] = new CachedTree
		{
			Tree = tree,
			Path = primary,
			LastWrite = stamp
		};
		return tree;
	}

	private static DialogTree? LoadUncached(string traderId, string primary)
	{
		// .dlg 剧本格式优先；解析失败回退 .json
		if (primary.EndsWith(".dlg", StringComparison.OrdinalIgnoreCase))
		{
			DialogTree? script = TryLoadScript(primary, traderId);
			if (script != null) return script;
		}
		string path = Path.Combine(ConfigDir, traderId + ".json");
		if (!File.Exists(path)) return null;
		try
		{
			DialogTree? tree = JsonConvert.DeserializeObject<DialogTree>(File.ReadAllText(path));
			if (tree == null || tree.Nodes.Count == 0) return null;
			VisitPlugin.Log.LogInfo((object)$"DialogTreeLoader: loaded '{path}' ({tree.Nodes.Count} nodes)");
			return tree;
		}
		catch (Exception ex)
		{
			VisitPlugin.Log.LogWarning((object)("DialogTreeLoader: failed to parse '" + path + "': " + ex.Message));
			return null;
		}
	}

	private static DialogTree? TryLoadScript(string path, string traderId)
	{
		try
		{
			List<string> errors = new List<string>();
			DialogTree? tree = DialogScriptParser.Parse(
				File.ReadAllLines(path, System.Text.Encoding.UTF8), Path.GetFileName(path), errors);
			foreach (string error in errors)
			{
				VisitPlugin.Log.LogWarning((object)("DialogTreeLoader: " + error));
			}
			if (tree == null || tree.Nodes.Count == 0) return null;
			if (string.IsNullOrEmpty(tree.TraderId)) tree.TraderId = traderId;
			VisitPlugin.Log.LogInfo((object)$"DialogTreeLoader: loaded '{path}' ({tree.Nodes.Count} nodes, dlg)");
			return tree;
		}
		catch (Exception ex)
		{
			VisitPlugin.Log.LogWarning((object)("DialogTreeLoader: failed to parse '" + path + "': " + ex.Message));
			return null;
		}
	}

	public static string? ResolvePath(string? rawPath)
	{
		if (string.IsNullOrWhiteSpace(rawPath)) return null;
		return Path.IsPathRooted(rawPath) ? rawPath : Path.Combine(ConfigDir, rawPath);
	}
}
