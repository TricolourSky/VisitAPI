using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EFT;
using EFT.Dialogs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VisitAPI.Native;

/// <summary>
/// 1.0 零售 `dialogue.json` 的客户端侧读取。注意分工（这不是 BUG，是既有事实）：
/// **原生 narrate 路径的模板由服务端 mod 灌进任务数据库下发**，客户端只需要 `SeedVariables`（种"相识"变量）；
/// `Load()`（把模板/локale 灌进 DialogStorage）只服务自定义 `.dlg` 的 `@visit` 跳转。
/// </summary>
public static class RetailDialogs
{
    static Dictionary<string, MongoID> _entries;
    static List<MongoID> _seeds;
    static Dictionary<string, List<MongoID>> _acquaint;   // 商人 id → 他自己的对话读的那些「相识」变量（种子 ∩ 该商人模板用到的）
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
        var profile = ((IDialogContext)dc).Profile;
        foreach (var v in _seeds)
        {
            // 档案里已经有值的（对话推进过的状态机，如 Skier 的 1→4）别用种子盖回去：会话值优先于档案值（09-08）
            if (profile != null && profile.ProfileVariables.GetVariableValue(v) != 0) continue;
            KnownSeeds.TryGetValue(v.ToString(), out var value);
            dc.SetVariableValue(new DialogSetVariableAction.SaveStateData(v, value, DialogLineTemplate.ESaveStateType.Session));
        }
    }

    /// <summary>「与 X 交谈」目标的落实（09-08，SORA 实机：Skier 那条永远做不完）。
    /// 1.1 的任务条件 GlobalVariableValue 读的是**档案变量**（`Profile.ProfileVariables`，ConditionsConnectorsManager 订阅它的 OnVariableChanged），
    /// 而写它的是 1.1 那批没抓到的任务对话；插件以前只把「相识」标志种进会话，档案里永远是 0。现在：真正打开这位商人的对话时，
    /// 把他自己那几个相识变量在档案里从 0 写成 1（写档案作用域 → 条件当场刷新）并同步到服务端 pmc.Variables（重登不丢）。
    /// 只在 0 → 1 这一步动手；对话自己往后推的状态（2/3/4）一律不碰。</summary>
    public static void MarkAcquainted(BaseTraderDialogController dc, string traderId)
    {
        Scan();
        if (traderId == null || !_acquaint.TryGetValue(traderId, out var vars) || vars.Count == 0) return;
        var profile = ((IDialogContext)dc).Profile;
        if (profile == null) return;
        foreach (var v in vars)
        {
            if (profile.ProfileVariables.GetVariableValue(v) != 0) continue;
            dc.SetVariableValue(new DialogSetVariableAction.SaveStateData(v, 1, DialogLineTemplate.ESaveStateType.Profile));
            Vars.Sync(v, 1);
            Plugin.Log.LogInfo($"[retail] 与 {traderId} 相识：档案变量 {v} 0 -> 1（任务「与其交谈」目标据此达成）");
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
        if (dto?.Elements == null)
        {
            _loaded = false;   // 解析失败别锁死：文件可能正被替换，下次调用重试（旧 N2）
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
        var usedBy = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);   // 商人 → 他的模板读到的变量
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
