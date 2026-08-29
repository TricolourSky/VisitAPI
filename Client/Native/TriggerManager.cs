using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using UnityEngine;
using VisitAPI.Dialog;

namespace VisitAPI.Native;

public static class TriggerManager
{
	private static float _next;

	private static bool _spawned;

	private static readonly List<GameObject> _live = new List<GameObject>();

	public static void Tick()
	{
		if (Time.unscaledTime < _next)
		{
			return;
		}
		_next = Time.unscaledTime + 1f;
		if (!Singleton<GameWorld>.Instantiated)
		{
			if (_spawned)
			{
				_live.Clear();
				_spawned = false;
			}
		}
		else
		{
			if (Singleton<GameWorld>.Instance is NarrateGameWorld || _spawned)
			{
				return;
			}
			string locationId = Singleton<GameWorld>.Instance.LocationId ?? "";
			if (locationId.Length == 0)
			{
				return;
			}
			_spawned = true;
			bool inHideout = locationId.IndexOf("hideout", StringComparison.OrdinalIgnoreCase) >= 0;
			foreach (DialogTree dialogTree in DialogFiles.All())
			{
				string traderId = dialogTree.TraderId;
				foreach (DialogTrigger trigger in dialogTree.Triggers)
				{
					bool matches;
					if (!inHideout)
					{
						if (!(trigger.Kind == "raid"))
						{
							continue;
						}
						matches = MapMatches(trigger.Place, locationId);
					}
					else
					{
						matches = trigger.Kind == "hideout";
					}
					if (matches)
					{
						Spawn(traderId, trigger, inHideout);
					}
				}
			}
			Plugin.Log.LogInfo($"[trigger] {locationId}: 生成 {_live.Count} 个触发点");
		}
	}

	private static void Spawn(string traderId, DialogTrigger tr, bool hideout)
	{
		GameObject gameObject = new GameObject("VisitTrigger_" + traderId);
		VisitTrigger visitTrigger = gameObject.AddComponent<VisitTrigger>();
		visitTrigger.TraderId = traderId;
		visitTrigger.Data = tr;
		visitTrigger.Merge = hideout && !tr.Free;
		visitTrigger.RequireLook = (!hideout || tr.Free) && !visitTrigger.Auto;
		_live.Add(gameObject);
	}

	private static bool MapMatches(string place, string loc)
	{
		return place == "*" || loc.IndexOf(place, StringComparison.OrdinalIgnoreCase) >= 0 || place.IndexOf(loc, StringComparison.OrdinalIgnoreCase) >= 0;
	}
}
