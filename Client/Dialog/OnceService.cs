using System;
using System.Collections.Generic;
using System.IO;
using EFT;
using Newtonsoft.Json;
using VisitAPI.Dialog;

namespace VisitAPI.Native;

public static class OnceService
{
    static readonly HashSet<string> _migrated = new();

    static MongoID Id(string kind, string trader, string key) => DialogTemplateBuilder.Id("visitapi.once", kind + "|" + trader + "|" + key);
    public static MongoID OnceId(string trader, string node, int option) => Id("once", trader, node + "|" + option);
    public static MongoID FirstId(string trader) => Id("first", trader, "");
    public static MongoID TriggerId(string trader, string key) => Id("trig", trader, key);

    public static bool Used(Profile profile, MongoID id)
    {
        if (profile?.ProfileVariables == null) return false;
        Migrate(profile);
        return profile.ProfileVariables.GetVariableValue(id) != 0;
    }

    public static void Mark(Profile profile, MongoID id, string what)
    {
        if (profile?.ProfileVariables == null) return;
        if (profile.ProfileVariables.GetVariableValue(id) == 1) return;
        profile.ProfileVariables.SetVariableValue(id, 1);
        Vars.Sync(id, 1);
        Plugin.Log.LogDebug($"[once] {what} 记号已打（档案变量 {id}）");
    }

    public static void Mark(Profile profile, (string trader, string node, int option) key) =>
        Mark(profile, OnceId(key.trader, key.node, key.option), $"{key.node}#{key.option}");

    internal static void Migrate(Profile profile)
    {
        if (profile?.ProfileVariables == null) return;
        var pid = profile.Id;
        if (string.IsNullOrEmpty(pid) || !_migrated.Add(pid)) return;
        try { MigrateCore(profile, pid); }
        catch (Exception e) { Plugin.Log.LogWarning($"[once] 旧记号迁移失败（不影响对话）: {e.Message}"); }
    }

    static void MigrateCore(Profile profile, string pid)
    {
        var dir = DialogFiles.Loader.BaseDir;
        if (!Directory.Exists(dir)) return;
        var moved = 0;
        foreach (var file in Directory.GetFiles(dir, "*.seen.json"))
        {
            var trader = Path.GetFileName(file); trader = trader.Substring(0, trader.Length - ".seen.json".Length);
            Dictionary<string, int> map;
            try { map = JsonConvert.DeserializeObject<Dictionary<string, int>>(File.ReadAllText(file)); }
            catch (Exception e) { Plugin.Log.LogWarning($"[once] 旧记号文件 {Path.GetFileName(file)} 解析失败，跳过: {e.Message}"); continue; }
            if (map == null) continue;
            var prefix = pid + "|";
            foreach (var key in map.Keys)
            {
                if (!key.StartsWith(prefix, StringComparison.Ordinal)) continue;
                var rest = key.Substring(prefix.Length);
                MongoID? id = null;
                if (rest == "first") id = FirstId(trader);
                else if (rest.StartsWith("once|", StringComparison.Ordinal))
                {
                    var body = rest.Substring(5); var cut = body.LastIndexOf('|');
                    if (cut > 0 && int.TryParse(body.Substring(cut + 1), out var opt)) id = OnceId(trader, body.Substring(0, cut), opt);
                }
                else if (rest.StartsWith("trig|", StringComparison.Ordinal)) id = TriggerId(trader, rest.Substring(5));
                if (id == null || profile.ProfileVariables.GetVariableValue(id.Value) != 0) continue;
                profile.ProfileVariables.SetVariableValue(id.Value, 1);
                Vars.Sync(id.Value, 1);
                moved++;
            }
        }
        if (moved > 0) Plugin.Log.LogInfo($"[once] 旧 seen.json 里 {moved} 个记号已搬进档案变量（档案 {pid}）");
    }
}
