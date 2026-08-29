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
    static bool _loaded;

    // 1.0 里由服务端 profile 同步的"相识"标志, 数据文件内无人写入; 不置 1 则走不进正常对话(缺选项/踩进残缺的初见线)
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
        foreach (var v in _seeds)
        {
            KnownSeeds.TryGetValue(v.ToString(), out var value);
            dc.SetVariableValue(new DialogSetVariableAction.SaveStateData(v, value, DialogLineTemplate.ESaveStateType.Session));
        }
    }

    static string DialogueJsonPath => Path.Combine(BepInEx.Paths.PluginPath, "VisitAPI", "scenes", "bundles", "vendors", "dialogue.json");

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
        if (dto?.Elements == null) { Plugin.Log.LogWarning("[retail] dialogue.json parse failed"); return; }
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
        foreach (var el in JObject.Parse(text)["elements"] ?? new JArray())
        {
            if (el.Value<bool?>("IsStart") == true)
            {
                var trader = el.Value<string>("Trader");
                var id = el.Value<string>("Id");
                if (trader != null && id != null && !_entries.ContainsKey(trader)) _entries[trader] = new MongoID(id);
            }
            foreach (var line in el["Lines"] ?? new JArray())
            {
                Collect(line["Trigger"], used);
                foreach (var act in line["Actions"] ?? new JArray())
                    if (act.Value<string>("type") == "SetVariable" && act.Value<string>("variableId") is string w) written.Add(w);
            }
        }
        foreach (var v in used.Except(written)) _seeds.Add(new MongoID(v));
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
