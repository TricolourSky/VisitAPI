using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using UnityEngine;
using VisitAPI.Dialog;

namespace VisitAPI.Native;

public static class TriggerHost
{
    static float _next;
    static GameWorld _spawnedFor;
    static readonly List<GameObject> _spawned = new();

    public static void Tick()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 1f;
        InputGuard.Tick();
        if (!Singleton<GameWorld>.Instantiated) { _spawnedFor = null; return; }
        var world = Singleton<GameWorld>.Instance;
        if (Narrating.IsVisitWorld(world) || ReferenceEquals(world, _spawnedFor)) return;
        var locationId = world.LocationId ?? "";
        if (locationId.Length == 0) return;
        foreach (var old in _spawned) if (old != null) UnityEngine.Object.Destroy(old);
        _spawned.Clear();
        _spawnedFor = world;
        var inHideout = Raid.IsHideout(locationId);
        foreach (var tree in DialogFiles.All())
            foreach (var trigger in tree.Triggers)
            {
                var matches = inHideout
                    ? trigger.Kind == "hideout"
                    : trigger.Kind == "raid" && MapMatches(trigger.Place, locationId);
                if (matches) Spawn(tree.TraderId, trigger, inHideout);
            }
    }

    static void Spawn(string traderId, DialogTrigger tr, bool hideout)
    {
        var go = new GameObject("VisitTrigger_" + traderId);
        var t = go.AddComponent<VisitTrigger>();
        t.TraderId = traderId;
        t.Data = tr;
        t.Merge = hideout && !tr.Free;
        t.RequireLook = (!hideout || tr.Free) && !t.Auto;
        _spawned.Add(go);
        if (!hideout) MapMarker(tr);
    }

    static void MapMarker(DialogTrigger tr)
    {
        try
        {
            if (tr.Enter >= 0f) return;
            var questId = tr.IfQuestId ?? tr.FinishId;
            if (string.IsNullOrEmpty(questId)) return;
            var quest = QuestOps.Resolve()?.Quests?.GetConditional(questId);
            if (quest?.Template == null) return;
            if (quest.QuestStatus != EFT.Quests.EQuestStatus.Started) return;
            if (!quest.Template.Conditions.TryGetValue(EFT.Quests.EQuestStatus.AvailableForFinish, out var conds)) return;
            var targets = new List<string>();
            foreach (var c in conds)
            {
                if (c is EFT.Quests.ConditionVisitPlace vp && !string.IsNullOrEmpty(vp.target)) targets.Add(vp.target);
                if (c is EFT.Quests.ConditionCounterCreator cc && cc._templateConditions?.Conditions != null)
                    foreach (var inner in cc._templateConditions.Conditions)
                        if (inner is EFT.Quests.ConditionVisitPlace ivp && !string.IsNullOrEmpty(ivp.target)) targets.Add(ivp.target);
            }
            foreach (var id in targets.Distinct())
            {
                var go = new GameObject("VisitMapMarker_" + id);
                go.transform.position = new Vector3(tr.X, tr.Y, tr.Z);
                var box = go.AddComponent<BoxCollider>();
                box.isTrigger = true; box.size = new Vector3(0.01f, 0.01f, 0.01f);
                box.enabled = false;
                go.AddComponent<EFT.Interactive.ExperienceTrigger>().SetId(id);
                go.layer = LayerMask.NameToLayer("Triggers");
                _spawned.Add(go);
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning("[trigger] failed to create map markers (triggers unaffected): " + e.Message); }
    }

    static bool MapMatches(string place, string loc) =>
        place == "*" || loc.IndexOf(place, StringComparison.OrdinalIgnoreCase) >= 0 || place.IndexOf(loc, StringComparison.OrdinalIgnoreCase) >= 0;
}
