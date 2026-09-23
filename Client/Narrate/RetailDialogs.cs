using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EFT;
using EFT.Dialogs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VisitAPI.Native;

public static class RetailDialogs
{
    static Dictionary<string, MongoID> _entries;
    static List<MongoID> _seeds;
    static Dictionary<string, List<MongoID>> _acquaint;
    static bool _loaded;

    static readonly Dictionary<string, int> KnownSeeds = new(StringComparer.Ordinal)
    {
        { "68c81cf8d242d0b184959530", 1 },
        { "68c41b22c5e8a18a3692d395", 1 },
        { "68c2bf4f8c7b5c191d5ec0df", 1 },
    };

    public static bool TryGetEntry(string traderId, out MongoID entry)
    {
        Load();
        return _entries.TryGetValue(traderId, out entry);
    }

    public static void SeedVariables(BaseTraderDialogController dc)
    {
        Scan();
        var profile = ((IDialogContext)dc).Profile;
        foreach (var v in _seeds)
        {
            if (profile != null && profile.ProfileVariables.GetVariableValue(v) != 0) continue;
            if (WrittenByLoadedDialog(v)) continue;
            KnownSeeds.TryGetValue(v.ToString(), out var value);
            dc.SetVariableValue(new DialogSetVariableAction.SaveStateData(v, value, DialogLineTemplate.ESaveStateType.Session));
        }
    }

    public static void MarkAcquainted(BaseTraderDialogController dc, string traderId)
    {
        Scan();
        if (traderId == null || !_acquaint.TryGetValue(traderId, out var vars) || vars.Count == 0) return;
        var profile = ((IDialogContext)dc).Profile;
        if (profile == null) return;
        foreach (var v in vars)
        {
            if (profile.ProfileVariables.GetVariableValue(v) != 0) continue;
            if (WrittenByLoadedDialog(v)) continue;
            dc.SetVariableValue(new DialogSetVariableAction.SaveStateData(v, 1, DialogLineTemplate.ESaveStateType.Profile));
            Vars.Sync(v, 1);
            Plugin.Log.LogInfo($"[retail] 与 {traderId} 相识：档案变量 {v} 0 -> 1（任务「与其交谈」目标据此达成）");
        }
    }

    static bool WrittenByLoadedDialog(MongoID v)
    {
        var templates = DialogStorage.Instance?._dialogTemplates;
        if (templates == null) return false;
        foreach (var t in templates.Values)
            if (t?.Lines != null)
                foreach (var line in t.Lines)
                    if (line?.Actions != null)
                        foreach (var a in line.Actions)
                            if (a is DialogSetVariableAction set && set.Variable.VariableId == v)
                            {
                                Plugin.Log.LogInfo($"[retail] 相识变量 {v} 由对话 {t.Id} 自己写，插件不代写");
                                return true;
                            }
        return false;
    }

    /// <summary>零售对话表的客户端副本，和房间文件住一起（rooms\，老目录见 VisitPaths）。</summary>
    static string DialogueJsonPath => Path.Combine(VisitPaths.Rooms, "dialogue.json");

    static void Scan()
    {
        if (_seeds != null) return;
        _entries = new Dictionary<string, MongoID>(StringComparer.OrdinalIgnoreCase);
        _seeds = new List<MongoID>();
        if (File.Exists(DialogueJsonPath)) ScanRaw(File.ReadAllText(DialogueJsonPath));
    }

    static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        Scan();
        var path = DialogueJsonPath;
        if (!File.Exists(path)) { Plugin.Log.LogWarning("[retail] dialogue.json not found: " + path); return; }
        var text = File.ReadAllText(path);
        var settings = new JsonSerializerSettings { Error = (_, e) => e.ErrorContext.Handled = true, Converters = { new NestedLocaleConverter() } };
        var dto = JsonConvert.DeserializeObject<TraderDialogsDTO>(text, settings);
        if (dto?.Elements == null)
        {
            _loaded = false;
            Plugin.Log.LogWarning("[retail] dialogue.json parse failed");
            return;
        }
        var locales = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in dto.Elements.Where(e => e?.LocalizationDictionary != null))
            foreach (var loc in t.LocalizationDictionary)
            {
                if (loc.Value == null) continue;
                if (!locales.TryGetValue(loc.Key, out var merged)) merged = locales[loc.Key] = new Dictionary<string, string>();
                foreach (var kv in loc.Value) merged[kv.Key] = kv.Value;
            }
        DialogStorage.Instance.AddTemplates(dto.Elements.Where(e => e != null));
        if (LocalizationManager.Instance != null)
            foreach (var loc in locales) LocalizationManager.Instance.UpdateLocales(loc.Key, loc.Value);
        foreach (var id in _entries.Values)
            if (DialogStorage.Instance.TryGetTemplate(id, out var t)) t.CanBeFirstDialog = true;
        Plugin.Log.LogDebug($"[retail] {dto.Elements.Length} templates, entries for {_entries.Count} trader(s), {_seeds.Count} seeded variable(s), locales: {string.Join(",", locales.Keys)}");
    }

    static void ScanRaw(string text)
    {
        var written = new HashSet<string>();
        var used = new HashSet<string>();
        var usedBy = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var el in JObject.Parse(text)["elements"] ?? new JArray())
        {
            var trader = el.Value<string>("Trader");
            if (el.Value<bool?>("IsStart") == true)
            {
                var id = el.Value<string>("Id");
                if (trader != null && id != null && !_entries.ContainsKey(trader)) _entries[trader] = new MongoID(id);
            }
            var mine = trader != null ? (usedBy.TryGetValue(trader, out var s) ? s : usedBy[trader] = new HashSet<string>()) : null;
            foreach (var line in el["Lines"] ?? new JArray())
            {
                Collect(line["Trigger"], used);
                if (mine != null) Collect(line["Trigger"], mine);
                foreach (var act in line["Actions"] ?? new JArray())
                    if (act.Value<string>("type") == "SetVariable" && act.Value<string>("variableId") is string w) written.Add(w);
            }
        }
        var seeds = new HashSet<string>(used.Except(written));
        foreach (var v in seeds) _seeds.Add(new MongoID(v));
        _acquaint = new Dictionary<string, List<MongoID>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (trader, vars) in usedBy)
            _acquaint[trader] = vars.Where(seeds.Contains).Where(KnownSeeds.ContainsKey).Select(v => new MongoID(v)).ToList();
    }

    static void Collect(JToken cond, HashSet<string> used)
    {
        if (cond == null || cond.Type != JTokenType.Object) return;
        if (cond.Value<string>("type") == "VariableValue" && cond.Value<string>("variableId") is string v) used.Add(v);
        foreach (var sub in cond["Conditions"] ?? new JArray()) Collect(sub, used);
    }

    sealed class NestedLocaleConverter : JsonConverter
    {
        public override bool CanConvert(Type t) => t == typeof(IReadOnlyDictionary<string, Dictionary<string, string>>);
        public override bool CanWrite => false;
        public override object ReadJson(JsonReader r, Type t, object v, JsonSerializer s) => r.TokenType == JsonToken.Null ? null : s.Deserialize<Dictionary<string, Dictionary<string, string>>>(r);
        public override void WriteJson(JsonWriter w, object v, JsonSerializer s) => s.Serialize(w, v);
    }
}
