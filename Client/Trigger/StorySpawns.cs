using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace VisitAPI.Native;

/// <summary>剧情刷兵点（10-01 SORA 拍板：侦查任务进行中，西楼地下室开锁门前站 4-5 只黑狐）。
/// 数据在包的 spawns\*.json（服务端 /visitapi/spawns 原样转发）：一条 = 地图 + 圆心/半径 + 兵种 + 数量 + 任务门（状态）。
/// 做法照官方 BotSpawner.DebugSpawnAnyway：BotCreationData.Create → AddPosition（导航网格采样的定点）→ method_10 激活；
/// 兵种名运行时 Enum.Parse——SPT 给模组兵种扩展过 WildSpawnType（黑狐的原生 Boss 波就是这么进游戏的），
/// 解析不动就整条放弃并写警告，绝不拿原版兵种顶包。minZ/maxZ/minX/maxX 是可选硬边界：圆心搭在锁门线外，
/// 采样吸附后还要再验一次边界和楼层（圆心地面高度 ±1.2 米），保证一个点都不落进锁着的门后或吸到楼下。
/// Fika 联机只有主机刷（客机跳过）；一场战局一条最多刷一次。</summary>
public static class StorySpawns
{
    class Entry
    {
        public string Map, Quest, Role, Difficulty;
        public List<int> Statuses = new();
        public int Min = 1, Max = 1;
        public Vector3 At;
        public float Radius = 4f;
        public float? MinX, MaxX, MinZ, MaxZ;
        public float[] Hours;   // 可选时段门（战局内时钟，跨零点写法同原生 daytime，如 21→6）
    }

    static List<Entry> _list = new();

    public static void Prefetch() => Plugin.Instance.StartCoroutine(VisitHttp.Fetch("/visitapi/spawns", TryParse, "[spawns]", _ => { }));

    static bool TryParse(string body)
    {
        try
        {
            if (!(JObject.Parse(body)["data"] is JArray arr)) return false;
            var parsed = new List<Entry>();
            foreach (var t in arr.OfType<JObject>())
            {
                try
                {
                    var e = new Entry
                    {
                        Map = t["map"]?.Value<string>(),
                        Quest = t["quest"]?.Value<string>(),
                        Role = t["role"]?.Value<string>(),
                        Difficulty = t["difficulty"]?.Value<string>() ?? "normal",
                        Statuses = (t["statuses"] as JArray)?.Select(x => x.Value<int>()).ToList() ?? new List<int>(),
                        Min = t["min"]?.Value<int>() ?? 1,
                        Max = t["max"]?.Value<int>() ?? (t["min"]?.Value<int>() ?? 1),
                        Radius = t["radius"]?.Value<float>() ?? 4f,
                        MinX = t["minX"]?.Value<float>(), MaxX = t["maxX"]?.Value<float>(),
                        MinZ = t["minZ"]?.Value<float>(), MaxZ = t["maxZ"]?.Value<float>(),
                    };
                    if (t["hours"] is JObject hh) e.Hours = new[] { hh["from"]?.Value<float>() ?? 0f, hh["to"]?.Value<float>() ?? 0f };
                    if (t["at"] is JObject at) e.At = new Vector3(at["x"]?.Value<float>() ?? 0f, at["y"]?.Value<float>() ?? 0f, at["z"]?.Value<float>() ?? 0f);
                    if (string.IsNullOrEmpty(e.Map) || string.IsNullOrEmpty(e.Role)) { Plugin.Log.LogWarning("[spawns] an entry is missing map or role, skipped: " + t.ToString(Newtonsoft.Json.Formatting.None)); continue; }
                    parsed.Add(e);
                }
                catch (Exception ex) { Plugin.Log.LogWarning("[spawns] failed to parse an entry, skipped: " + ex.Message); }
            }
            _list = parsed;
            return true;
        }
        catch (Exception e) { Plugin.Log.LogWarning("[spawns] parse failed: " + e.Message); return false; }
    }

    public static void Spawn(GameWorld world)
    {
        var loc = world?.LocationId;
        if (string.IsNullOrEmpty(loc) || _list.Count == 0) return;
        foreach (var e in _list)
            if (string.Equals(e.Map, loc, StringComparison.OrdinalIgnoreCase))
                Plugin.Instance.StartCoroutine(SpawnOne(e));
    }

    /// Fika 联机：客机不刷（刷兵是主机的事）。没装 Fika、或版本变了读不出 IsServer，就按主机处理——SORA 单人开着 Fika 时自己就是主机。
    /// 类型路径 2.4.x 在 Main.Utils（10-01 反编译核对；最初写成老版的 Coop.Utils，反射扑空 = 整条静默跳过，黑狐不出现的根源之一）
    static bool FikaGuest()
    {
        try
        {
            var t = Type.GetType("Fika.Core.Main.Utils.FikaBackendUtils, Fika.Core")
                 ?? Type.GetType("Fika.Core.Coop.Utils.FikaBackendUtils, Fika.Core");
            var p = t?.GetProperty("IsServer", BindingFlags.Public | BindingFlags.Static);
            if (p?.GetValue(null) is bool isServer) return !isServer;
            return false;
        }
        catch { return false; }
    }

    static IEnumerator SpawnOne(Entry e)
    {
        // 等原生刷兵系统就绪（姿势同区域探针：等游戏真正开局，再等刷兵器有了创建器）
        var deadline = Time.unscaledTime + 90f;
        BotSpawner spawner = null;
        while (Time.unscaledTime < deadline)
        {
            if (Singleton<AbstractGame>.Instantiated && Singleton<AbstractGame>.Instance.Status == GameStatus.Started
                && Singleton<IBotGame>.Instantiated
                && (spawner = Singleton<IBotGame>.Instance?.BotsController?._botSpawner) != null
                && spawner._botCreator != null) break;
            spawner = null;
            yield return new WaitForSeconds(1f);
        }
        if (spawner == null) { Plugin.Log.LogWarning($"[spawns] bot system not ready after 90 s; story spawn skipped ({e.Role} @ {e.Map})"); yield break; }
        if (FikaGuest()) yield break;
        if (!GatePasses(e)) yield break;
        if (!QuestZones.HoursOk(e.Hours)) yield break;

        WildSpawnType role;
        try { role = (WildSpawnType)Enum.Parse(typeof(WildSpawnType), e.Role, true); }
        catch { Plugin.Log.LogWarning($"[spawns] bot role '{e.Role}' is unknown to this game install (is its mod loaded?); story spawn skipped"); yield break; }
        BotDifficulty dif;
        try { dif = (BotDifficulty)Enum.Parse(typeof(BotDifficulty), e.Difficulty ?? "normal", true); }
        catch { dif = BotDifficulty.normal; }

        var want = UnityEngine.Random.Range(e.Min, e.Max + 1);
        var points = Points(e, want);
        if (points.Count == 0) { Plugin.Log.LogWarning($"[spawns] no walkable spot inside r={e.Radius} around {e.At} (bounds applied); story spawn skipped"); yield break; }
        if (points.Count < want) Plugin.Log.LogWarning($"[spawns] only {points.Count}/{want} walkable spots found around {e.At}; spawning fewer bots");

        var (zone, core) = Anchor(spawner, e.At);
        if (zone == null) { Plugin.Log.LogWarning("[spawns] no bot zone on this map; story spawn skipped"); yield break; }

        var task = Build(spawner, role, dif, points.Count);
        while (!task.IsCompleted && Time.unscaledTime < deadline + 60f) yield return null;
        if (!task.IsCompleted || task.IsFaulted || task.Result == null)
        {
            Plugin.Log.LogWarning("[spawns] bot profiles did not generate; story spawn skipped" + (task.IsFaulted ? ": " + task.Exception?.GetBaseException().Message : ""));
            yield break;
        }
        var data = task.Result;
        foreach (var p in points) data.AddPosition(p, core);
        // 10-01 实测教训：method_10 一次只激活一只（官方 DebugSpawnAnyway 就是单只用法）——
        // 首版整包只调一次，五只只出一只。现在一点一只，隔一帧一个，位置按 AddPosition 的队列逐个取
        var want2 = Math.Min(points.Count, data.Count);
        var spawned = 0;
        for (var i = 0; i < want2; i++)
        {
            try { spawner.method_10(zone, data, Hold, spawner.GetCancelToken()); spawned++; }
            catch (Exception ex) { Plugin.Log.LogWarning("[spawns] activation failed: " + ex.Message); break; }
            yield return null;
        }
        if (spawned > 0) Plugin.Log.LogInfo($"[spawns] story spawn: {spawned} x {e.Role} near ({e.At.x:0.#}, {e.At.y:0.#}, {e.At.z:0.#}), holding position");
    }

    /// 守卫驻点：激活完成就暂停巡逻（原生 PatrollingData.Pause），不再被巡逻路线拉走；交战反应仍归 AI 本体 / SAIN
    static void Hold(BotOwner bot)
    {
        try { bot?.PatrollingData?.Pause(); }
        catch (Exception ex) { Plugin.Log.LogWarning("[spawns] could not hold a guard at its post: " + ex.Message); }
    }

    static bool GatePasses(Entry e)
    {
        if (string.IsNullOrEmpty(e.Quest)) return true;
        var quest = QuestOps.Resolve()?.Quests?.GetConditional(e.Quest);
        return quest != null && (e.Statuses.Count == 0 || e.Statuses.Contains((int)quest.QuestStatus));
    }

    static async System.Threading.Tasks.Task<BotCreationData> Build(BotSpawner spawner, WildSpawnType role, BotDifficulty dif, int count)
    {
        var spawnParams = new BotSpawnParams { ShallBeGroup = new ShallBeGroupParams(true, false, count) };
        var profileData = new GetProfileDataParams(EPlayerSide.Savage, role, dif, 0f, spawnParams, keepZoneOnSpawn: true);
        return await BotCreationData.Create(profileData, spawner._botCreator, count, spawner);
    }

    /// 圆内取点：随机点 → 边界（锁门线）→ 导航网格吸附 → 吸附后再验边界 + 同一层楼 → 点距 ≥1.2 米。
    /// 10-01 实测：地下室锁门后的刷卡闸比门前走廊低 3 米多，吸附会把点拉到楼下去——以圆心处的地面高度为基准，高差超 1.2 米的点不要
    static List<Vector3> Points(Entry e, int count)
    {
        var pts = new List<Vector3>();
        bool InBounds(Vector3 v) =>
            (!e.MinX.HasValue || v.x >= e.MinX.Value) && (!e.MaxX.HasValue || v.x <= e.MaxX.Value) &&
            (!e.MinZ.HasValue || v.z >= e.MinZ.Value) && (!e.MaxZ.HasValue || v.z <= e.MaxZ.Value);
        var floorY = NavMesh.SamplePosition(e.At, out var centre, 3f, NavMesh.AllAreas) ? centre.position.y : e.At.y;
        for (var i = 0; i < 48 && pts.Count < count; i++)
        {
            var c = UnityEngine.Random.insideUnitCircle * e.Radius;
            var cand = e.At + new Vector3(c.x, 0f, c.y);
            if (!InBounds(cand)) continue;
            if (!NavMesh.SamplePosition(cand, out var hit, 2f, NavMesh.AllAreas)) continue;
            if (!InBounds(hit.position) || Mathf.Abs(hit.position.y - floorY) > 1.2f) continue;
            if (pts.Any(p => (p - hit.position).sqrMagnitude < 1.44f)) continue;
            pts.Add(hit.position);
        }
        return pts;
    }

    /// 选锚点：在全部区域的刷兵点里找离圆心最近的那一个，用它的区域 + CorePointId。
    /// 10-01 实测教训：按区域物体的 transform 找"最近区域"不可靠（很多区域的原点和它的实际范围不在一处），
    /// 首版把守卫锚到了湖那头，AI 顺着巡逻路线就走了——必须按点位找
    static (BotZone zone, int core) Anchor(BotSpawner spawner, Vector3 at)
    {
        BotZone bz = null;
        var core = 0;
        var bd = float.MaxValue;
        foreach (var z in spawner._allBotZones ?? Array.Empty<BotZone>())
        {
            var markers = z?.SpawnPointMarkers;
            if (markers == null) continue;
            foreach (var m in markers)
            {
                if (m?.SpawnPoint == null) continue;
                var d = (m.SpawnPoint.Position - at).sqrMagnitude;
                if (d < bd) { bd = d; bz = z; core = m.SpawnPoint.CorePointId; }
            }
        }
        return (bz, core);
    }
}

/// 进图开局挂一次（姿势同任务区域：访问世界不算）
[HarmonyPatch(typeof(GameWorld), nameof(GameWorld.OnGameStarted))]
public static class StorySpawnStart
{
    static void Postfix(GameWorld __instance)
    {
        try
        {
            if (Narrating.IsVisitWorld(__instance) || Narrating.Now) return;
            StorySpawns.Spawn(__instance);
        }
        catch (Exception e) { Plugin.Log.LogError("[spawns] failed to start story spawns (raid unaffected): " + e); }
    }
}
