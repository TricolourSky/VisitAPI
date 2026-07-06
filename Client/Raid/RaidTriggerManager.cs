using System;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace VisitAPI
{
    internal static class RaidTriggerManager
    {
        private static bool _spawned;
        private static float _nextCheck;

        internal static void Tick()
        {
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 1f;

            if (!Singleton<GameWorld>.Instantiated)
            {
                if (_spawned) QuestItemSpawner.Reset();
                _spawned = false;
                return;
            }
            if (_spawned) return;

            GameWorld gw = Singleton<GameWorld>.Instance;
            string loc = gw.LocationId ?? "";
            if (loc.Length == 0) return;
            bool hideout = loc.IndexOf("hideout", StringComparison.OrdinalIgnoreCase) >= 0;

            int count = 0;
            foreach (string traderId in Plugin.RegisteredTraders)
            {
                DialogTree? tree = DialogTreeLoader.Load(traderId);
                if (tree == null) continue;
                if (hideout)
                {
                    if (tree.HideoutTriggers == null) continue;
                    foreach (HideoutAreaTrigger h in tree.HideoutTriggers)
                    {
                        if (h.Offset == null || h.Offset.Length < 3) continue;
                        SpawnHideout(traderId, h);
                        count++;
                    }
                }
                else
                {
                    foreach (FirstVisitTrigger t in tree.AllRaidTriggers())
                    {
                        if (!MapMatches(loc, t.Map)) continue;
                        bool npc = !string.IsNullOrEmpty(t.NpcName);
                        if (!npc && (t.Position == null || t.Position.Length < 3)) continue;
                        SpawnRaid(traderId, t, npc);
                        count++;
                    }
                    count += QuestItemSpawner.SpawnAll(tree, loc);
                }
            }
            _spawned = true;
            Plugin.Log.LogInfo("[VisitTrigger] location '" + loc + "': spawned " + count + " trigger(s)/item(s)");
        }

        internal static bool MapMatches(string loc, string map)
        {
            if (string.IsNullOrEmpty(map) || map == "*") return true;
            return loc.IndexOf(map, StringComparison.OrdinalIgnoreCase) >= 0
                || map.IndexOf(loc, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void SpawnRaid(string traderId, FirstVisitTrigger t, bool npc)
        {
            VisitTrigger trig = NewTrigger(traderId, t.PromptText);
            trig.MaxDistance = t.MaxDistance;
            trig.HitRadius = t.HitRadius;
            trig.RequireLook = true;
            if (npc)
            {
                trig.NpcName = t.NpcName;
                trig.Node = t.Node;
                trig.QuestId = t.QuestId;
                trig.ShowWhenStatus = t.ShowWhenStatus;
                Plugin.Log.LogInfo("[VisitTrigger] npc trigger '" + trig.PromptText + "' (" + traderId + ") follows '" + t.NpcName + "' on '" + t.Map + "'");
            }
            else
            {
                trig.FixedPosition = new Vector3(t.Position![0], t.Position[1], t.Position[2]);
                Plugin.Log.LogInfo("[VisitTrigger] raid trigger '" + trig.PromptText + "' (" + traderId + ") at " + trig.FixedPosition + " on '" + t.Map + "'");
            }
        }

        private static void SpawnHideout(string traderId, HideoutAreaTrigger h)
        {
            VisitTrigger trig = NewTrigger(traderId, h.PromptText);
            trig.FixedPosition = new Vector3(h.Offset![0], h.Offset[1], h.Offset[2]);
            trig.MaxDistance = h.MaxDistance;
            trig.HitRadius = h.HitRadius;
            trig.Node = h.Node;
            trig.QuestId = h.QuestId;
            trig.ShowWhenStatus = h.ShowWhenStatus;
            trig.MergeIntoNativeMenu = !h.FreeStanding;
            trig.RequireLook = h.FreeStanding;
            Plugin.Log.LogInfo("[VisitTrigger] hideout trigger '" + trig.PromptText + "' (" + traderId + ") at " + trig.FixedPosition + " area '" + h.AreaType + "' -> node '" + (h.Node ?? "(default)") + "'");
        }

        private static VisitTrigger NewTrigger(string traderId, string prompt)
        {
            GameObject go = new GameObject("VisitTrigger_" + traderId);
            VisitTrigger trig = go.AddComponent<VisitTrigger>();
            trig.TraderId = traderId;
            trig.PromptText = string.IsNullOrEmpty(prompt) ? Loc.DefaultVisitPrompt : prompt;
            return trig;
        }
    }
}
