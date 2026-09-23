using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace VisitAPI.Native;

public static class QuestZones
{
    public class Zone
    {
        public string Id, Type;
        public List<string> Locations = new();
        public Vector3 Position, Size = Vector3.one;
        public Quaternion Rotation = Quaternion.identity;
        public Subtitle Subtitles;
    }

    public class Subtitle
    {
        public List<RaidSubtitles.Line> Lines = new();
        public string Quest;
        public List<int> Statuses = new();
        public bool OncePerRaid = true;
        public Interact Interact;
        public List<Variant> Pool = new();
        public float Volume = 0.8f;

        public Variant Pick()
        {
            if (Pool.Count > 0) return Pool[UnityEngine.Random.Range(0, Pool.Count)];
            return new Variant { Lines = Lines };
        }
    }

    public class Variant { public string Audio; public List<RaidSubtitles.Line> Lines = new(); }

    public class Interact
    {
        public string Prompt = "使用";
        public float Dist = 2.5f, Radius = 0.6f;
        public Vector3? At;
        public bool CompleteOnUse = true;
    }

    static List<Zone> _zones = new();
    static readonly List<GameObject> _live = new();

    public static void Prefetch() => Plugin.Instance.StartCoroutine(VisitHttp.Fetch("/visitapi/zones", TryParse, "[zones]", _ => { }));

    static bool TryParse(string body)
    {
        try
        {
            if (!(JObject.Parse(body)["data"] is JArray arr)) return false;
            var parsed = new List<Zone>();
            foreach (var t in arr.OfType<JObject>())
            {
                try
                {
                    var z = new Zone
                    {
                        Id = t["id"]?.Value<string>(), Type = (t["type"]?.Value<string>() ?? "visit").ToLowerInvariant(),
                        Locations = (t["locations"] as JArray)?.Select(x => x.Value<string>()).Where(s => !string.IsNullOrEmpty(s)).ToList() ?? new List<string>(),
                        Position = V3(t["position"], Vector3.zero), Size = V3(t["size"], Vector3.one),
                        Rotation = t["rotation"] is JObject r ? new Quaternion(F(r, "x"), F(r, "y"), F(r, "z"), r["w"] != null ? F(r, "w") : 1f) : Quaternion.identity
                    };
                    if (string.IsNullOrEmpty(z.Id) || z.Locations.Count == 0) { Plugin.Log.LogWarning("[zones] 有一条区域缺 id 或 locations，跳过: " + t.ToString(Newtonsoft.Json.Formatting.None)); continue; }
                    if (t["subtitles"] is JObject s)
                    {
                        var sub = new Subtitle
                        {
                            Quest = s["quest"]?.Value<string>(),
                            Statuses = (s["statuses"] as JArray)?.Select(x => x.Value<int>()).ToList() ?? new List<int>(),
                            OncePerRaid = !string.Equals(s["once"]?.Value<string>(), "always", StringComparison.OrdinalIgnoreCase)
                        };
                        static List<RaidSubtitles.Line> Lines(JToken arr) => (arr as JArray)?.OfType<JObject>()
                            .Select(l => new RaidSubtitles.Line { Key = l["key"]?.Value<string>(), Start = l["start"]?.Value<float>() ?? 0f, End = l["end"]?.Value<float>() ?? 5f })
                            .Where(l => !string.IsNullOrEmpty(l.Key)).ToList() ?? new List<RaidSubtitles.Line>();
                        sub.Lines = Lines(s["lines"]);
                        sub.Volume = s["volume"]?.Value<float>() ?? 0.8f;
                        foreach (var v in (s["pool"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                            sub.Pool.Add(new Variant { Audio = v["audio"]?.Value<string>(), Lines = Lines(v["lines"]) });
                        if (s["interact"] is JObject it)
                            sub.Interact = new Interact
                            {
                                Prompt = it["prompt"]?.Value<string>() is string p && p.Length > 0 ? p : "使用",
                                Dist = it["dist"]?.Value<float>() ?? 2.5f, Radius = it["radius"]?.Value<float>() ?? 0.6f,
                                At = it["at"] is JObject at ? V3(at, z.Position) : (Vector3?)null,
                                CompleteOnUse = it["completeOnUse"]?.Value<bool>() ?? true
                            };
                        if (sub.Lines.Count > 0 || sub.Pool.Count > 0) z.Subtitles = sub;
                        else Plugin.Log.LogWarning($"[zones] 区域 {z.Id} 的 subtitles 既没有 lines 也没有 pool，忽略");
                    }
                    parsed.Add(z);
                }
                catch (Exception e) { Plugin.Log.LogWarning("[zones] 一条区域解析失败，跳过: " + e.Message); }
            }
            _zones = parsed;
            Plugin.Log.LogInfo($"[zones] 登记 {parsed.Count} 个任务区域（{string.Join(", ", parsed.SelectMany(z => z.Locations).Distinct(StringComparer.OrdinalIgnoreCase))}）");
            return true;
        }
        catch (Exception e) { Plugin.Log.LogWarning("[zones] parse failed: " + e.Message); return false; }
    }

    static float F(JObject o, string k) => o[k]?.Value<float>() ?? 0f;
    static Vector3 V3(JToken t, Vector3 fallback) => t is JObject o ? new Vector3(F(o, "x"), F(o, "y"), F(o, "z")) : fallback;

    public static void Spawn(GameWorld world)
    {
        foreach (var go in _live) if (go != null) UnityEngine.Object.Destroy(go);
        _live.Clear();
        var loc = world?.LocationId;
        if (string.IsNullOrEmpty(loc) || _zones.Count == 0) return;
        var made = new List<string>();
        foreach (var z in _zones.Where(z => z.Locations.Any(l => string.Equals(l, loc, StringComparison.OrdinalIgnoreCase))))
        {
            var go = new GameObject(z.Id);
            go.transform.SetPositionAndRotation(z.Position, z.Rotation);
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = z.Size;
            TriggerWithId trigger = z.Type switch
            {
                "placeitem" => go.AddComponent<PlaceItemTrigger>(),
                _ => go.AddComponent<ExperienceTrigger>(),
            };
            trigger.SetId(z.Id);
            go.layer = LayerMask.NameToLayer("Triggers");
            var probe = go.AddComponent<QuestZoneProbe>();
            probe.Id = z.Id; probe.Zone = z;
            _live.Add(go);
            made.Add($"{z.Id}({z.Type})");
            if (z.Subtitles?.Interact is Interact it)
            {
                if (it.CompleteOnUse)
                {
                    box.enabled = false;
                    probe.ManualOnly = true;
                }
                var at = it.At ?? z.Position;
                var tgo = new GameObject("VisitZoneTrigger_" + z.Id);
                var t = tgo.AddComponent<VisitTrigger>();
                t.TraderId = "zone:" + z.Id;
                t.Data = new VisitAPI.Dialog.DialogTrigger
                {
                    Kind = "raid", Place = loc, X = at.x, Y = at.y, Z = at.z, Dist = it.Dist, Radius = it.Radius, Prompt = it.Prompt,
                    IfQuestId = z.Subtitles.Quest, IfStatuses = new List<int>(z.Subtitles.Statuses)
                };
                t.RequireLook = true;
                t.Voice = z.Subtitles;
                t.VoiceAt = at;
                if (it.CompleteOnUse) t.ZoneToComplete = trigger;
                _live.Add(tgo);
                made.Add($"{z.Id}:交互「{it.Prompt}」@({at.x:0.#},{at.y:0.#},{at.z:0.#})");
            }
        }
        if (made.Count > 0) Plugin.Log.LogInfo($"[zones] {loc}: 生成 {made.Count} 个任务区域 [{string.Join(", ", made)}]");
    }
}

public class QuestZoneProbe : MonoBehaviour
{
    public string Id;
    public QuestZones.Zone Zone;
    public bool ManualOnly;
    bool _inside, _subtitled;

    void PlaySubtitle(string how)
    {
        try
        {
            var sub = Zone?.Subtitles;
            if (sub == null || sub.Interact != null || (_subtitled && sub.OncePerRaid)) return;
            if (!string.IsNullOrEmpty(sub.Quest))
            {
                var quest = QuestOps.Resolve()?.Quests?.GetConditional(sub.Quest);
                if (quest == null || (sub.Statuses.Count > 0 && !sub.Statuses.Contains((int)quest.QuestStatus)))
                { Plugin.Log.LogInfo($"[zones] {Id} 的字幕不播：任务 {sub.Quest} 现在是 {(quest == null ? "不在任务书里" : quest.QuestStatus.ToString())}"); return; }
            }
            _subtitled = true;
            var pick = sub.Pick();
            if (!string.IsNullOrEmpty(pick.Audio)) RaidVoice.PlayAt(pick.Audio, transform.position, sub.Volume);
            RaidSubtitles.Play(pick.Lines, $"任务区域 {Id}，{how}");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"[zones] {Id} 播字幕失败: " + e.Message); }
    }

    System.Collections.IEnumerator Start()
    {
        if (ManualOnly) yield break;
        for (var i = 0; i < 120; i++)
        {
            var player = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer : null;
            if (player != null)
            {
                for (var j = 0; j < 240; j++)
                {
                    if (Singleton<AbstractGame>.Instantiated && Singleton<AbstractGame>.Instance.Status == GameStatus.Started) break;
                    yield return new WaitForSeconds(0.5f);
                }
                yield return new WaitForSeconds(2f);
                player = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer : null;
                if (player == null) yield break;
                var box = GetComponent<BoxCollider>();
                var local = transform.InverseTransformPoint(player.Position);
                var half = (box != null ? box.size : Vector3.one) * 0.5f;
                if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z && !_inside)
                {
                    _inside = true;
                    GetComponent<TriggerWithId>()?.TriggerEnter(player);
                    Plugin.Log.LogInfo($"[zones] 玩家出生就在任务区域 {Id} 里，补触发一次（原生只认走进去那一下）");
                    PlaySubtitle("出生就在里面");
                }
                yield break;
            }
            yield return new WaitForSeconds(0.5f);
        }
        Plugin.Log.LogWarning($"[zones] 等了 60 秒主玩家没就位，没做出生检查：{Id}");
    }

    void OnTriggerEnter(Collider col)
    {
        try
        {
            if (!Singleton<GameWorld>.Instantiated) return;
            var world = Singleton<GameWorld>.Instance;
            var player = world.GetPlayerByCollider(col);
            if (player == null || !ReferenceEquals(player, world.MainPlayer)) return;
            _inside = true;
            Plugin.Log.LogInfo($"[zones] 玩家进入任务区域 {Id}");
            PlaySubtitle("走进区域");
        }
        catch {  }
    }
}

[HarmonyPatch(typeof(GameWorld), nameof(GameWorld.OnGameStarted))]
public static class QuestZoneSpawn
{
    static void Postfix(GameWorld __instance)
    {
        try
        {
            if (__instance is NarrateGameWorld || Narrating.Now) return;
            QuestZones.Spawn(__instance);
        }
        catch (Exception e) { Plugin.Log.LogError("[zones] 生成任务区域失败（战局不受影响）: " + e); }
    }
}
