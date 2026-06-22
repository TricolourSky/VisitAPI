using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VisitAPI
{
    internal static class RaidTriggerManager
    {
        private const BindingFlags All = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static bool _resolved;
        private static PropertyInfo? _instanceProp;
        private static PropertyInfo? _instantiatedProp;
        private static bool _spawned;
        private static float _nextCheck;

        internal static void Tick()
        {
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 1f;

            object? gw = GetGameWorld();
            if (gw == null)
            {
                _spawned = false;
                return;
            }
            if (_spawned) return;

            string loc = GetLocationId(gw);
            if (string.IsNullOrEmpty(loc)) return;

            int count = 0;
            foreach (string traderId in Plugin.RegisteredTraders)
            {
                DialogTree? tree = DialogTreeLoader.Load(traderId);
                if (tree == null) continue;
                foreach (FirstVisitTrigger t in tree.AllRaidTriggers())
                {
                    if (t.Position == null || t.Position.Length < 3) continue;
                    if (!MapMatches(loc, t.Map)) continue;
                    Spawn(traderId, t);
                    count++;
                }
            }
            if (loc.IndexOf("hideout", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                foreach (string traderId in Plugin.RegisteredTraders)
                {
                    DialogTree? tree = DialogTreeLoader.Load(traderId);
                    if (tree?.HideoutTriggers == null) continue;
                    foreach (HideoutAreaTrigger h in tree.HideoutTriggers)
                    {
                        if (h.Offset == null || h.Offset.Length < 3) continue;
                        SpawnHideout(traderId, h);
                        count++;
                    }
                }
            }
            _spawned = true;
            Plugin.Log.LogInfo("[RaidTrigger] raid location '" + loc + "': spawned " + count + " trigger(s)");
        }

        private static object? GetGameWorld()
        {
            if (!_resolved)
            {
                _resolved = true;
                Type? open = AccessTools.TypeByName("Comfort.Common.Singleton`1");
                Type? gwType = AccessTools.TypeByName("EFT.GameWorld");
                if (open != null && gwType != null)
                {
                    Type closed = open.MakeGenericType(gwType);
                    _instanceProp = closed.GetProperty("Instance", All);
                    _instantiatedProp = closed.GetProperty("Instantiated", All);
                }
                if (_instanceProp == null)
                    Plugin.Log.LogWarning("[RaidTrigger] Singleton<GameWorld>.Instance not found; raid triggers disabled");
            }
            if (_instanceProp == null) return null;
            try
            {
                if (_instantiatedProp != null && !(bool)_instantiatedProp.GetValue(null)) return null;
                return _instanceProp.GetValue(null);
            }
            catch
            {
                return null;
            }
        }

        private static string GetLocationId(object gw)
        {
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type t = gw.GetType();
            object? v = t.GetProperty("LocationId", inst)?.GetValue(gw) ?? t.GetField("LocationId", inst)?.GetValue(gw);
            return v as string ?? "";
        }

        private static bool MapMatches(string loc, string map)
        {
            if (string.IsNullOrEmpty(map) || map == "*") return true;
            return loc.IndexOf(map, StringComparison.OrdinalIgnoreCase) >= 0
                || map.IndexOf(loc, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void Spawn(string traderId, FirstVisitTrigger t)
        {
            GameObject go = new GameObject("VisitTrigger_" + traderId);
            RaidVisitTrigger trig = go.AddComponent<RaidVisitTrigger>();
            trig.TraderId = traderId;
            trig.PromptText = string.IsNullOrEmpty(t.PromptText) ? Loc.DefaultVisitPrompt : t.PromptText;
            trig.TriggerPosition = new Vector3(t.Position[0], t.Position[1], t.Position[2]);
            trig.MaxDistance = t.MaxDistance;
            trig.HitRadius = t.HitRadius;
            Plugin.Log.LogInfo("[RaidTrigger] '" + t.PromptText + "' (" + traderId + ") at " + trig.TriggerPosition + " for map '" + t.Map + "'");
        }

        private static void SpawnHideout(string traderId, HideoutAreaTrigger h)
        {
            GameObject go = new GameObject("VisitHideoutTrigger_" + traderId);
            HideoutVisitTrigger trig = go.AddComponent<HideoutVisitTrigger>();
            trig.TraderId = traderId;
            trig.PromptText = string.IsNullOrEmpty(h.PromptText) ? Loc.DefaultVisitPrompt : h.PromptText;
            trig.TriggerPosition = new Vector3(h.Offset![0], h.Offset[1], h.Offset[2]);
            trig.MaxDistance = h.MaxDistance;
            trig.Node = h.Node;
            trig.QuestId = h.QuestId;
            trig.ShowWhenStatus = h.ShowWhenStatus;
            trig.FreeStanding = h.FreeStanding;
            trig.HitRadius = h.HitRadius;
            Plugin.Log.LogInfo("[HideoutTrigger] '" + h.PromptText + "' (" + traderId + ") at " + trig.TriggerPosition + " area '" + h.AreaType + "' -> node '" + (h.Node ?? "(default)") + "'");
        }
    }
}
