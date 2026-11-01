using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace VisitAPI.Native;

/// <summary>剧情刷兵点（10-01 SORA 拍板：侦查任务进行中，西楼地下室开锁门前站 4-5 只黑狐）。
/// 数据在包的 spawns\*.json（服务端 /visitapi/spawns 原样转发）：一条 = 地图 + 圆心/半径 + 兵种 + 数量 + 任务门（状态）。
/// 做法照官方 BotSpawner.DebugSpawnAnyway：BotCreationData.Create → AddPosition（导航网格采样的定点）→ method_10 激活；
/// 兵种名运行时 Enum.TryParse——SPT 给模组兵种扩展过 WildSpawnType（黑狐的原生 Boss 波就是这么进游戏的），
/// 解析不动就整条放弃并写警告，绝不拿原版兵种顶包。minZ/maxZ/minX/maxX 是可选硬边界：圆心搭在锁门线外，
/// 采样吸附后还要再验一次边界和楼层（圆心地面高度 ±1.2 米），保证一个点都不落进锁着的门后或吸到楼下。
/// Fika 联机只有主机刷（客机跳过）；一场战局一条最多刷一次。
/// 10-24：等刷兵器 / 选锚点 / 生成 / 逐只激活这一段是 WaitSpawner + Activate，护卫（Escort）也走它。</summary>
public static class StorySpawns
{
    class Entry
    {
        public string Map, Quest, Role, Difficulty;
        public List<int> Statuses = new();
        public int Min = 1, Max = 1;
        public Vector3 At;
        public bool HasAt;
        public float Radius = 4f;
        public float? MinX, MaxX, MinZ, MaxZ;
        public float[] Hours;   // 可选时段门（战局内时钟，跨零点写法同原生 daytime，如 21→6）
        // 10-17「看门狗」：id + on: "event" = 开局不刷，等计时区（QuestZoneStay）的事件点名再刷；attack = 刷出来就冲着触发事件的玩家去；
        // 没写 at 时，从离事件中心 distance.min～max 米、玩家看不见的原生刷兵点里挑一个当圆心（「从外面突入」）
        public string Id;
        public bool OnEvent, Attack;
        public float DistMin = 25f, DistMax = 70f;
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
                        Hours = QuestZones.Hours(t["hours"]),
                    };
                    if (t["at"] is JObject) { e.At = QuestZones.V3(t["at"], Vector3.zero); e.HasAt = true; }
                    e.Id = t["id"]?.Value<string>();
                    e.OnEvent = string.Equals(t["on"]?.Value<string>(), "event", StringComparison.OrdinalIgnoreCase);
                    e.Attack = t["attack"]?.Value<bool>() ?? false;
                    if (t["distance"] is JObject dd) { e.DistMin = dd["min"]?.Value<float>() ?? e.DistMin; e.DistMax = dd["max"]?.Value<float>() ?? e.DistMax; }
                    if (string.IsNullOrEmpty(e.Map) || string.IsNullOrEmpty(e.Role)) { Plugin.Log.LogWarning("[spawns] an entry is missing map or role, skipped: " + t.ToString(Newtonsoft.Json.Formatting.None)); continue; }
                    // 开局就刷的条目没写 at 就不知道刷在哪（以前静默拿原点当圆心）；事件条目可以不写（从事件中心外围挑）
                    if (!e.HasAt && !e.OnEvent) { Plugin.Log.LogWarning("[spawns] an entry has no 'at' and is not an 'on: event' entry, skipped: " + t.ToString(Newtonsoft.Json.Formatting.None)); continue; }
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
            if (!e.OnEvent && string.Equals(e.Map, loc, StringComparison.OrdinalIgnoreCase))
                Plugin.Instance.StartCoroutine(SpawnOne(e));
    }

    /// 计时区事件点名刷一条（on: event 的条目）。near = 事件中心（计时区盒心），target = 触发事件的玩家（attack 冲着他去）
    public static void Fire(string id, Vector3 near, Player target)
    {
        var loc = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.LocationId : null;
        var e = _list.Find(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Map, loc, StringComparison.OrdinalIgnoreCase));
        if (e == null) { Plugin.WarnOnce("spawn-event-" + id, $"[spawns] no spawn entry '{id}' for this map; the event spawns nothing"); return; }
        Plugin.Instance.StartCoroutine(SpawnOne(e, near, target));
    }

    /// Fika 联机：客机不刷（刷兵是主机的事）。没装 Fika、或版本变了读不出 IsServer，就按主机处理——SORA 单人开着 Fika 时自己就是主机。
    /// 类型路径 2.4.x 在 Main.Utils（10-01 反编译核对；最初写成老版的 Coop.Utils，反射扑空 = 整条静默跳过，黑狐不出现的根源之一）
    internal static bool FikaGuest()
    {
        try
        {
            var t = Type.GetType("Fika.Core.Main.Utils.FikaBackendUtils, Fika.Core")
                 ?? Type.GetType("Fika.Core.Coop.Utils.FikaBackendUtils, Fika.Core");
            var p = t?.GetProperty("IsServer", BindingFlags.Public | BindingFlags.Static);
            return p?.GetValue(null) is bool isServer && !isServer;
        }
        catch { return false; }
    }

    /// 等原生刷兵系统就绪（战局真正开局、刷兵器有了创建器），最多 90 秒；到点还没有就给 null
    internal static IEnumerator WaitSpawner(Action<BotSpawner> result)
    {
        var deadline = Time.unscaledTime + 90f;
        BotSpawner spawner = null;
        while (Time.unscaledTime < deadline)
        {
            if (Raid.Started && Singleton<IBotGame>.Instantiated
                && (spawner = Singleton<IBotGame>.Instance?.BotsController?._botSpawner) != null
                && spawner._botCreator != null) break;
            spawner = null;
            yield return new WaitForSeconds(1f);
        }
        result(spawner);
    }

    static IEnumerator SpawnOne(Entry e, Vector3? near = null, Player target = null)
    {
        if (FikaGuest()) yield break;   // 10-24：客机先判，不白等 90 秒
        BotSpawner spawner = null;
        yield return WaitSpawner(s => spawner = s);
        if (spawner == null) { Plugin.Log.LogWarning($"[spawns] bot system not ready after 90 s; story spawn skipped ({e.Role} @ {e.Map})"); yield break; }
        if (!QuestOps.StatusIn(e.Quest, e.Statuses)) yield break;
        if (!QuestZones.HoursOk(e.Hours)) yield break;

        if (!Enum.TryParse(e.Role, true, out WildSpawnType role)) { Plugin.Log.LogWarning($"[spawns] bot role '{e.Role}' is unknown to this game install (is its mod loaded?); story spawn skipped"); yield break; }
        if (!Enum.TryParse(e.Difficulty, true, out BotDifficulty dif)) dif = BotDifficulty.normal;

        var want = UnityEngine.Random.Range(e.Min, e.Max + 1);
        var at = e.At;
        if (!e.HasAt && near.HasValue)
        {
            var picked = Outside(spawner, near.Value, e.DistMin, e.DistMax, target);
            if (picked == null) { Plugin.Log.LogWarning($"[spawns] no spawn spot {e.DistMin:0}-{e.DistMax:0} m from {near.Value} out of the player's sight; event spawn '{e.Id}' skipped"); yield break; }
            at = picked.Value;
        }
        else if (e.HasAt && target != null)
        {
            // 10-17：写死的突入点（F11 取的楼梯口）正在玩家眼里时，等它被挡住再刷（最多 10 秒），免得当面凭空冒出来
            var waitUntil = Time.unscaledTime + 10f;
            while (Time.unscaledTime < waitUntil && !HiddenFrom(target, at)) yield return new WaitForSeconds(0.5f);
        }
        var points = PointsAround(at, e.Radius, want, v =>
            (!e.MinX.HasValue || v.x >= e.MinX.Value) && (!e.MaxX.HasValue || v.x <= e.MaxX.Value) &&
            (!e.MinZ.HasValue || v.z >= e.MinZ.Value) && (!e.MaxZ.HasValue || v.z <= e.MaxZ.Value));
        if (points.Count == 0) { Plugin.Log.LogWarning($"[spawns] no walkable spot inside r={e.Radius} around {at} (bounds applied); story spawn skipped"); yield break; }
        if (points.Count < want) Plugin.Log.LogWarning($"[spawns] only {points.Count}/{want} walkable spots found around {at}; spawning fewer bots");

        Action<BotOwner> activated = e.Attack && target != null ? b => Attack(b, target) : Hold;
        yield return Activate(spawner, role, dif, points, at, null, null, activated, "[spawns]", "story spawn", _ => { });
    }

    /// <summary>在这些点上刷一组：选锚点 → 生成档案（最多等 60 秒）→ 每点 AddPosition → method_10 一帧激活一只
    ///（10-01 实测教训：method_10 一次只激活一只，官方 DebugSpawnAnyway 就是单只用法——首版整包只调一次，五只只出一只）。
    /// beforeActivate 在档案生成好、激活之前调（护卫改名、登记归属）；done 给实际激活的只数；tag / what 只用来写日志</summary>
    internal static IEnumerator Activate(BotSpawner spawner, WildSpawnType role, BotDifficulty dif, List<Vector3> points, Vector3 at, string spawnId,
        Action<BotCreationData> beforeActivate, Action<BotOwner> activated, string tag, string what, Action<int> done)
    {
        var (zone, core) = Anchor(spawner, at);
        if (zone == null) { Plugin.Log.LogWarning($"{tag} no bot zone on this map; {what} skipped"); done(0); yield break; }
        var task = Build(spawner, role, dif, points.Count, spawnId);
        var deadline = Time.unscaledTime + 60f;
        while (!task.IsCompleted && Time.unscaledTime < deadline) yield return null;
        if (!task.IsCompleted || task.IsFaulted || task.Result == null)
        {
            Plugin.Log.LogWarning($"{tag} bot profiles did not generate; {what} skipped" + (task.IsFaulted ? ": " + task.Exception?.GetBaseException().Message : ""));
            done(0); yield break;
        }
        var data = task.Result;
        beforeActivate?.Invoke(data);
        foreach (var p in points) data.AddPosition(p, core);
        var want = Math.Min(points.Count, data.Count);
        if (data.Count < points.Count) Plugin.Log.LogWarning($"{tag} only {data.Count}/{points.Count} bot profiles generated; spawning fewer {what}");
        var spawned = 0;
        for (var i = 0; i < want; i++)
        {
            try { spawner.method_10(zone, data, activated, spawner.GetCancelToken()); spawned++; }
            catch (Exception ex) { Plugin.Log.LogWarning($"{tag} activation failed: " + ex.Message); break; }
            yield return null;
        }
        done(spawned);
    }

    /// 突入：激活后把触发事件的玩家记成这组的敌人、报上他的位置（SAIN 的敌人表从 BotsGroup 来），装了 SAIN 的话性格调成敢推的；
    /// 突入层（EscortBrain.RushLayer，插件自己托管）在看见人之前一路往玩家那里压，看见了交给兵自己的 AI 打（SAIN 或原版）
    static void Attack(BotOwner bot, Player target)
    {
        try
        {
            var g = bot?.BotsGroup;
            if (g != null && Raid.Alive(target))
            {
                g.AddEnemy(target, EBotEnemyCause.addPlayer);
                g.ReportAboutEnemy(target, EEnemyPartVisibleType.Sence, bot);
            }
        }
        catch (Exception ex) { Plugin.WarnOnce("spawn-attack", "[spawns] could not point the attackers at the player: " + ex.Message); }
        StoryRush.Add(bot, target);
        BotLayers.Ensure(bot);   // 大脑已经激活了就当场插突入层；还没激活的由大脑激活后缀插（BotLayers.Attach）
        Plugin.Instance.StartCoroutine(EscortSain.Aggressive(bot));
    }

    /// 从 eye 到点 p（抬高 1.4 米，人站着的胸口）之间有墙 / 地形挡着
    static bool Hidden(Vector3 eye, Vector3 p) => Physics.Linecast(eye, p + Vector3.up * 1.4f, LayersMaskController.HighPolyWithTerrainMask);

    /// 从玩家眼睛看不见点 p（先把 F11 给的相机高度贴到地面）
    static bool HiddenFrom(Player target, Vector3 p)
    {
        if (target == null) return true;
        try
        {
            if (NavMesh.SamplePosition(p, out var floor, 3f, NavMesh.AllAreas)) p = floor.position;
            return Hidden(target.Position + Vector3.up * 1.5f, p);
        }
        catch { return true; }
    }

    /// 「从外面突入」的圆心：原生刷兵点里离 near 在 min～max 米之间、从玩家眼睛看不见的那些里挑最近的；
    /// 原生点都不合适就在同一圈距离里随机找导航网格点（同样要看不见）。都没有返回 null
    static Vector3? Outside(BotSpawner spawner, Vector3 near, float min, float max, Player target)
    {
        var eye = (target != null ? target.Position : near) + Vector3.up * 1.5f;
        bool InRing(Vector3 p) { var d = new Vector3(p.x - near.x, 0f, p.z - near.z).magnitude; return d >= min && d <= max; }
        Vector3? best = null;
        var bd = float.MaxValue;
        EachSpawnPoint(spawner, (_, p, _) =>
        {
            if (!InRing(p) || !Hidden(eye, p)) return;
            var d = (p - near).sqrMagnitude;
            if (d < bd) { bd = d; best = p; }
        });
        if (best.HasValue) return best;
        for (var i = 0; i < 64; i++)
        {
            var c = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(min, max);
            if (!NavMesh.SamplePosition(near + new Vector3(c.x, 0f, c.y), out var hit, 6f, NavMesh.AllAreas)) continue;
            if (InRing(hit.position) && Hidden(eye, hit.position)) return hit.position;
        }
        return null;
    }

    /// 守卫驻点：激活完成就暂停巡逻（原生 PatrollingData.Pause），不再被巡逻路线拉走；交战反应仍归 AI 本体 / SAIN
    static void Hold(BotOwner bot)
    {
        try { bot?.PatrollingData?.Pause(); }
        catch (Exception ex) { Plugin.Log.LogWarning("[spawns] could not hold a guard at its post: " + ex.Message); }
    }

    /// 10-16 护卫（Escort）也走这条：spawnId 写进 SpawnParams.Id_spawn——MoreBotsAPI 只给含 "hunt" 的刷兵挂猎杀，我们写别的名字就天然不猎杀
    internal static async System.Threading.Tasks.Task<BotCreationData> Build(BotSpawner spawner, WildSpawnType role, BotDifficulty dif, int count, string spawnId = null)
    {
        var spawnParams = new BotSpawnParams { ShallBeGroup = new ShallBeGroupParams(true, false, count), Id_spawn = spawnId ?? "visitapi_story" };
        var profileData = new GetProfileDataParams(EPlayerSide.Savage, role, dif, 0f, spawnParams, keepZoneOnSpawn: true);
        return await BotCreationData.Create(profileData, spawner._botCreator, count, spawner);
    }

    /// 圆内取点：随机点 → 边界（锁门线，SpawnOne 传进来）→ 导航网格吸附 → 吸附后再验边界 + 同一层楼 → 点距 ≥1.2 米。
    /// 10-01 实测：地下室锁门后的刷卡闸比门前走廊低 3 米多，吸附会把点拉到楼下去——以圆心处的地面高度为基准，高差超 1.2 米的点不要
    internal static List<Vector3> PointsAround(Vector3 at, float radius, int count, Func<Vector3, bool> inBounds = null)
    {
        var pts = new List<Vector3>();
        bool InBounds(Vector3 v) => inBounds == null || inBounds(v);
        var floorY = NavMesh.SamplePosition(at, out var centre, 3f, NavMesh.AllAreas) ? centre.position.y : at.y;
        // 10-17「看门狗」：刷兵点在楼梯间里（窄、有高差），按 ±1.2 米 / 间距 1.2 米可能凑不够人数——凑不够再放宽一轮（±1.8 米 / 0.7 米）
        foreach (var (height, gap) in new[] { (1.2f, 1.2f), (1.8f, 0.7f) })
            for (var i = 0; i < 48 && pts.Count < count; i++)
            {
                var c = UnityEngine.Random.insideUnitCircle * radius;
                var cand = at + new Vector3(c.x, 0f, c.y);
                if (!InBounds(cand)) continue;
                if (!NavMesh.SamplePosition(cand, out var hit, 2f, NavMesh.AllAreas)) continue;
                if (!InBounds(hit.position) || Mathf.Abs(hit.position.y - floorY) > height) continue;
                if (pts.Any(p => (p - hit.position).sqrMagnitude < gap * gap)) continue;
                pts.Add(hit.position);
            }
        return pts;
    }

    /// 全部区域的全部刷兵点：(区域, 点位, CorePointId)
    static void EachSpawnPoint(BotSpawner spawner, Action<BotZone, Vector3, int> each)
    {
        foreach (var z in spawner._allBotZones ?? Array.Empty<BotZone>())
        {
            var markers = z?.SpawnPointMarkers;
            if (markers == null) continue;
            foreach (var m in markers)
                if (m?.SpawnPoint != null) each(z, m.SpawnPoint.Position, m.SpawnPoint.CorePointId);
        }
    }

    /// 选锚点：在全部区域的刷兵点里找离圆心最近的那一个，用它的区域 + CorePointId。
    /// 10-01 实测教训：按区域物体的 transform 找"最近区域"不可靠（很多区域的原点和它的实际范围不在一处），
    /// 首版把守卫锚到了湖那头，AI 顺着巡逻路线就走了——必须按点位找
    internal static (BotZone zone, int core) Anchor(BotSpawner spawner, Vector3 at)
    {
        BotZone bz = null;
        var core = 0;
        var bd = float.MaxValue;
        EachSpawnPoint(spawner, (z, p, c) =>
        {
            var d = (p - at).sqrMagnitude;
            if (d < bd) { bd = d; bz = z; core = c; }
        });
        return (bz, core);
    }
}
