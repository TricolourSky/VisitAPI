using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VisitAPI;

internal static class QuestStatusCache
{
	private static readonly Dictionary<int, string> s_numToName = new Dictionary<int, string>
	{
		{ 0, "Locked" },
		{ 1, "AvailableForStart" },
		{ 2, "Started" },
		{ 3, "AvailableForFinish" },
		{ 4, "Success" },
		{ 5, "Fail" }
	};

	private static readonly Dictionary<string, int> s_nameToNum = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
	{
		{ "Locked", 0 },
		{ "AvailableForStart", 1 },
		{ "Started", 2 },
		{ "AvailableForFinish", 3 },
		{ "Success", 4 },
		{ "Fail", 5 }
	};

	private static readonly object s_lock = new object();

	private static readonly Dictionary<string, int> s_cache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	public static int GetStatus(string questId)
	{
		lock (s_lock)
		{
			int value;
			return (!s_cache.TryGetValue(questId, out value)) ? 1 : value;
		}
	}

	public static void Set(string questId, int status)
	{
		lock (s_lock)
		{
			s_cache[questId] = status;
		}
		if (s_numToName.TryGetValue(status, out string value))
		{
			VisitPlugin.Log.LogInfo((object)$"[QuestCache] {questId} → {status}({value})");
		}
	}

	public static bool IsVisible(DialogOption opt)
	{
		if (string.IsNullOrEmpty(opt.QuestId))
		{
			return true;
		}
		if (opt.ShowWhenStatus == null && opt.HideWhenStatus == null)
		{
			return true;
		}
		int status = GetStatus(opt.QuestId);
		if (opt.ShowWhenStatus != null && opt.ShowWhenStatus.Count > 0 && !AnyMatches(opt.ShowWhenStatus, status))
		{
			return false;
		}
		if (opt.HideWhenStatus != null && AnyMatches(opt.HideWhenStatus, status))
		{
			return false;
		}
		return true;
	}

	internal static bool AnyMatches(IEnumerable<string> tokens, int status)
	{
		foreach (string token in tokens)
		{
			if (MatchesStatus(token, status))
			{
				return true;
			}
		}
		return false;
	}

	internal static bool MatchesStatus(string token, int status)
	{
		if (s_nameToNum.TryGetValue(token, out var value))
		{
			return value == status;
		}
		if (int.TryParse(token, out var result))
		{
			return result == status;
		}
		return false;
	}

	public static void BatchFetch(string profileId, IEnumerable<string> questIds)
	{
		if (string.IsNullOrEmpty(profileId))
		{
			return;
		}
		// 原生任务状态优先：客户端 QuestController 是最准的来源（含外部 WTT 任务），覆盖写入缓存。
		// 下面的 HTTP 只补原生读不到的——多为锁定/未接触的任务，服务端会给 Locked，正合预期。
		Dictionary<string, int>? native = NativeQuestController.ReadNativeStatuses();
		if (native != null)
		{
			lock (s_lock)
			{
				foreach (KeyValuePair<string, int> entry in native)
				{
					// 取更靠后的状态：任务状态单调推进。原生（客户端任务书）可能滞后于
					// VisitAPI 旁路接取/完成（先改的是服务端档案），直接覆盖会把已接取的任务
					// 错误退回"可接取"。只在原生状态更靠后时才采用，兼顾 WTT 读取与旁路驱动。
					if (!s_cache.TryGetValue(entry.Key, out int cur) || entry.Value > cur)
					{
						s_cache[entry.Key] = entry.Value;
					}
				}
			}
		}
		List<string> list = new List<string>();
		lock (s_lock)
		{
			foreach (string questId in questIds)
			{
				if (!s_cache.ContainsKey(questId))
				{
					list.Add(questId);
				}
			}
		}
		if (list.Count == 0)
		{
			return;
		}
		try
		{
			// 手写序列化：不走 JsonConvert.SerializeObject，避免被其他 mod 篡改的
			// Newtonsoft 全局 DefaultSettings 影响（曾对普通 List<string> 误报 "Self referencing loop" 致查询失败）
			StringBuilder sb = new StringBuilder();
			sb.Append("{\"ProfileId\":").Append(JsonConvert.ToString(profileId)).Append(",\"QuestIds\":[");
			for (int i = 0; i < list.Count; i++)
			{
				if (i > 0)
				{
					sb.Append(",");
				}
				sb.Append(JsonConvert.ToString(list[i]));
			}
			sb.Append("]}");
			string data = sb.ToString();
			using WebClient webClient = new WebClient
			{
				Encoding = Encoding.UTF8
			};
			webClient.Headers["Content-Type"] = "application/json; charset=utf-8";
			JObject val = JObject.Parse(webClient.UploadString("http://127.0.0.1:6970/visitapi/quest/status", data));
			JToken obj = val["success"];
			if (obj == null || !Extensions.Value<bool>((IEnumerable<JToken>)obj))
			{
				return;
			}
			JToken obj2 = val["statuses"];
			JObject val2 = (JObject)(object)((obj2 is JObject) ? obj2 : null);
			if (val2 == null)
			{
				return;
			}
			lock (s_lock)
			{
				foreach (KeyValuePair<string, JToken> item in val2)
				{
					if (item.Value != null && int.TryParse(((object)item.Value).ToString(), out var result))
					{
						s_cache[item.Key] = result;
					}
				}
			}
			VisitPlugin.Log.LogInfo((object)$"[QuestCache] Fetched {((JContainer)val2).Count} statuses for {profileId}");
		}
		catch (Exception ex)
		{
			VisitPlugin.Log.LogWarning((object)("[QuestCache] BatchFetch failed: " + ex.Message));
		}
	}
}
