using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using Newtonsoft.Json;
using UnityEngine;

namespace VisitAPI;

internal static class RandomDialogStore
{
	private static readonly string StorePath = Path.Combine(BepInEx.Paths.ConfigPath, "VisitAPI", "pending_random.json");

	private static Dictionary<string, string> _pending = new Dictionary<string, string>();

	private static bool _loaded;

	internal static void RollForAllTraders()
	{
		EnsureLoaded();
		bool changed = false;
		foreach (string traderId in DialogTreeLoader.EnumerateTraderIds())
		{
			if (_pending.ContainsKey(traderId))
			{
				continue;
			}
			RandomAfterRaid randomAfterRaid = DialogTreeLoader.TryLoad(traderId)?.RandomAfterRaid;
			if (randomAfterRaid == null || randomAfterRaid.Nodes.Count == 0 || randomAfterRaid.Chance <= 0f)
			{
				continue;
			}
			float roll = UnityEngine.Random.value * 100f;
			if (roll >= randomAfterRaid.Chance)
			{
				continue;
			}
			string node = randomAfterRaid.Nodes[UnityEngine.Random.Range(0, randomAfterRaid.Nodes.Count)];
			_pending[traderId] = node;
			changed = true;
			VisitPlugin.Log.LogInfo((object)$"[RandomDialog] {traderId}: roll {roll:F1} < {randomAfterRaid.Chance} → pending node='{node}'");
		}
		if (changed)
		{
			Save();
		}
	}

	internal static string? ConsumePending(string traderId)
	{
		EnsureLoaded();
		if (!_pending.TryGetValue(traderId, out string value))
		{
			return null;
		}
		_pending.Remove(traderId);
		Save();
		VisitPlugin.Log.LogInfo((object)("[RandomDialog] Consumed pending node '" + value + "' for " + traderId));
		return value;
	}

	private static void EnsureLoaded()
	{
		if (_loaded)
		{
			return;
		}
		_loaded = true;
		try
		{
			if (File.Exists(StorePath))
			{
				_pending = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(StorePath)) ?? new Dictionary<string, string>();
			}
		}
		catch (Exception ex)
		{
			VisitPlugin.Log.LogWarning((object)("[RandomDialog] Load failed: " + ex.Message));
			_pending = new Dictionary<string, string>();
		}
	}

	private static void Save()
	{
		try
		{
			// 手写序列化，免疫其他 mod 对 Newtonsoft 全局 DefaultSettings 的篡改
			System.Text.StringBuilder sb = new System.Text.StringBuilder("{");
			bool first = true;
			foreach (KeyValuePair<string, string> kv in _pending)
			{
				if (!first)
				{
					sb.Append(",");
				}
				first = false;
				sb.Append(JsonConvert.ToString(kv.Key)).Append(":").Append(JsonConvert.ToString(kv.Value));
			}
			sb.Append("}");
			File.WriteAllText(StorePath, sb.ToString());
		}
		catch (Exception ex)
		{
			VisitPlugin.Log.LogWarning((object)("[RandomDialog] Save failed: " + ex.Message));
		}
	}
}
