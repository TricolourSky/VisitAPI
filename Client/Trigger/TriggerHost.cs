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
    static readonly List<(EFT.Interactive.ExperienceTrigger mark, string questId)> _markers = new();
    static bool _markerWarned;
    const string MarkerOff = "visitapi_marker_off";

    public static void Tick()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 1f;
        InputGuard.Tick();
        if (!Singleton<GameWorld>.Instantiated) { _spawnedFor = null; return; }
        var world = Singleton<GameWorld>.Instance;
        if (Narrating.IsVisitWorld(world)) return;
        if (ReferenceEquals(world, _spawnedFor)) { RefreshMarkers(); return; }
        var locationId = world.LocationId ?? "";
        if (locationId.Length == 0) return;
        foreach (var old in _spawned) if (old != null) UnityEngine.Object.Destroy(old);
        _spawned.Clear();
        _markers.Clear();
        _markerWarned = false;
        _spawnedFor = world;
        var inHideout = Raid.IsHideout(locationId);
        if (!inHideout) { DynamicMapsHidden.Ensure(); RaidRewardHold.EnsureFika(); }
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

    /// <summary>作者用编辑器写的任务，目标是占位的 VisitPlace（`visitapi_new_trigger`），真正推进靠 .dlg 触发线，场上没有同名触发器，Dynamic Maps 没点可画。
    /// 这里给每条带坐标的战局触发线配一个只给地图看的原生 ExperienceTrigger（碰撞体关着，不推进任何东西）。
    /// 10-01：Dynamic Maps 只在本局第一次开地图时抓一遍场上的 TriggerWithId，之后每次开图现读它们的 Id 和坐标——
    /// 所以标记进图就全部建好，Id 由 RefreshMarkers 每秒跟着任务状态切。以前只给进图那一刻已在进行中的任务建，局内才接的（初见 / 对峙 / 偶遇）图上没有点。</summary>
    static void MapMarker(DialogTrigger tr)
    {
        try
        {
            if (tr.Enter >= 0f) return;
            var questId = tr.IfQuestId ?? tr.FinishId;
            if (string.IsNullOrEmpty(questId)) return;
            var go = new GameObject("VisitMapMarker_" + questId);
            go.transform.position = new Vector3(tr.X, tr.Y, tr.Z);
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true; box.size = new Vector3(0.01f, 0.01f, 0.01f);
            box.enabled = false;
            var mark = go.AddComponent<EFT.Interactive.ExperienceTrigger>();
            mark.SetId(MarkerOff);
            go.layer = LayerMask.NameToLayer("Triggers");
            _spawned.Add(go);
            _markers.Add((mark, questId));
        }
        catch (Exception e) { Plugin.Log.LogWarning("[trigger] failed to create map markers (triggers unaffected): " + e.Message); }
    }

    static void RefreshMarkers()
    {
        if (_markers.Count == 0) return;
        try
        {
            var book = QuestOps.Resolve()?.Quests;
            foreach (var (mark, questId) in _markers)
            {
                if (mark == null) continue;
                var id = MarkerTarget(book?.GetConditional(questId)) ?? MarkerOff;
                if (mark.Id != id) mark.SetId(id);
            }
        }
        catch (Exception e)
        {
            if (_markerWarned) return;
            _markerWarned = true;
            Plugin.Log.LogWarning("[trigger] failed to refresh map markers (triggers unaffected): " + e.Message);
        }
    }

    /// 标记该顶的 id：任务进行中时，它第一个「已显示、还没完成、且没有真区域」的到达地点目标；别的情况返回空（标记熄掉）。
    /// 占位 id 各任务共用，不按状态熄掉会让别的任务也在这里出点；绑了真区域（zones\）的目标由区域自己出点。
    static string MarkerTarget(EFT.Quests.Quest quest)
    {
        if (quest?.Template?.Conditions == null || quest.QuestStatus != EFT.Quests.EQuestStatus.Started) return null;
        if (!quest.Template.Conditions.TryGetValue(EFT.Quests.EQuestStatus.AvailableForFinish, out var conds)) return null;
        foreach (var c in conds)
        {
            if (quest.IsConditionDone(c) || !quest.CheckVisibilityStatus(c)) continue;
            if (c is EFT.Quests.ConditionVisitPlace vp && Unbound(vp.target)) return vp.target;
            if (c is EFT.Quests.ConditionCounterCreator cc && cc._templateConditions?.Conditions != null)
                foreach (var inner in cc._templateConditions.Conditions)
                    if (inner is EFT.Quests.ConditionVisitPlace ivp && Unbound(ivp.target)) return ivp.target;
        }
        return null;
    }

    static bool Unbound(string target) => !string.IsNullOrEmpty(target) && !QuestZones.Defined(target);

    static bool MapMatches(string place, string loc) =>
        place == "*" || loc.IndexOf(place, StringComparison.OrdinalIgnoreCase) >= 0 || place.IndexOf(loc, StringComparison.OrdinalIgnoreCase) >= 0;
}
