using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace VisitAPI.Native;

public static class VariableGroups
{
    static readonly Dictionary<MongoID, List<MongoID>> _groups = new();
    static readonly Dictionary<MongoID, List<MongoID>> _byMember = new();

    public static void Fetch() => Plugin.Instance.StartCoroutine(VisitHttp.Fetch("/visitapi/variable/groups", TryParse, "[vargroups]", ok => { if (ok) RecomputeAll(); }));

    static bool TryParse(string body)
    {
        try
        {
            if (!(JObject.Parse(body)["data"] is JArray arr)) return false;
            lock (_groups)
            {
                _groups.Clear(); _byMember.Clear();
                foreach (var g in arr.OfType<JObject>())
                {
                    var id = g["id"]?.Value<string>();
                    if (id == null || id.Length != 24 || !(g["variables"] is JArray vs)) continue;
                    var gid = new MongoID(id);
                    var members = vs.Select(v => v.Value<string>()).Where(s => s != null && s.Length == 24).Select(s => new MongoID(s)).ToList();
                    if (members.Count == 0) continue;
                    _groups[gid] = members;
                    foreach (var m in members) { if (!_byMember.TryGetValue(m, out var l)) _byMember[m] = l = new List<MongoID>(); l.Add(gid); }
                }
            }
            Plugin.Log.LogDebug($"[vargroups] {_groups.Count} variable group(s) from server");
            return true;
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("[vargroups] parse failed: " + e.Message); return false; }
    }

    static ProfileVariablesStorage Storage()
    {
        try { return Singleton<ClientApplication<IEftSession>>.Instance?.GetClientBackEndSession()?.Profile?.ProfileVariables; }
        catch { return null; }
    }

    public static void RecomputeAll()
    {
        var s = Storage(); if (s == null) return;
        List<KeyValuePair<MongoID, List<MongoID>>> all; lock (_groups) all = _groups.ToList();
        foreach (var kv in all) Apply(s, kv.Key, kv.Value);
    }

    static void Apply(ProfileVariablesStorage s, MongoID group, List<MongoID> members)
    {
        try
        {
            var sum = members.Sum(m => s.GetVariableValue(m));
            if (s.GetVariableValue(group) == sum) return;
            s.SetVariableValue(group, sum);
            Vars.Sync(group, sum);
            Plugin.Log.LogInfo($"[vargroups] 变量组 {group} = {sum}（{members.Count} 个成员之和）");
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("[vargroups] 组求和失败: " + e.Message); }
    }

    [HarmonyPatch(typeof(ProfileVariablesStorage), nameof(ProfileVariablesStorage.SetVariableValue))]
    public static class OnSet
    {
        static void Postfix(ProfileVariablesStorage __instance, MongoID variableId)
        {
            List<MongoID> groups;
            lock (_groups) { if (!_byMember.TryGetValue(variableId, out groups)) return; groups = groups.ToList(); }
            foreach (var g in groups) if (_groups.TryGetValue(g, out var members)) Apply(__instance, g, members);
        }
    }
}
