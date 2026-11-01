using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using EFT;
using EFT.Dialogs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VisitAPI.Native;

public static class RetailDialogs
{
    // 09-24 审查 H3：三张表一开始就是空表，Scan 用局部变量建好再一次性换上。
    // rooms\dialogue.json 缺失或解析抛异常时表要是 null，访问里每进一个对话 MarkAcquainted 都空引用，打断引擎的对话流程
    static Dictionary<string, MongoID> _entries = new(StringComparer.OrdinalIgnoreCase);
    static List<MongoID> _seeds = new();
    static Dictionary<string, List<MongoID>> _acquaint = new(StringComparer.OrdinalIgnoreCase);
    static readonly object ScanGate = new();
    static bool _scanned;
    static bool _loaded;

    static readonly Dictionary<string, int> KnownSeeds = new(StringComparer.Ordinal)
    {
        { "68c81cf8d242d0b184959530", 1 },
        { "68c41b22c5e8a18a3692d395", 1 },
        { "68c2bf4f8c7b5c191d5ec0df", 1 },
    };

    /// 10-24：开游戏时在后台线程把 rooms\dialogue.json 扫一遍（只有文件 IO 和 JSON，不碰 Unity），第一次开对话 / 访问就不用当场扫
    public static void Prefetch() => Task.Run(Scan);

    public static bool TryGetEntry(string traderId, out MongoID entry)
    {
        Load();
        entry = default;
        return traderId != null && _entries.TryGetValue(traderId, out entry);
    }

    /// 只播种非零值（KnownSeeds 里只放非零值）。没写过的变量引擎本来就读成 0（GetVariableValue：对话作用域 → 会话作用域 → 档案，最后默认 0）；
    /// 把 0 也写进会话作用域的话，菜单对话控制器常驻、会话表整局不清，档案里这个变量后来变了也被这个 0 遮住（审查低项）
    public static void SeedVariables(BaseTraderDialogController dc)
    {
        Scan();
        if (dc == null) return;
        var profile = ((IDialogContext)dc).Profile;
        foreach (var v in _seeds)
        {
            if (!KnownSeeds.TryGetValue(v.ToString(), out var value)) continue;
            if (profile != null && profile.ProfileVariables.GetVariableValue(v) != 0) continue;
            if (WrittenByLoadedDialog(v)) continue;
            dc.SetVariableValue(new DialogSetVariableAction.SaveStateData(v, value, DialogLineTemplate.ESaveStateType.Session));
        }
    }

    public static void MarkAcquainted(BaseTraderDialogController dc, string traderId)
    {
        Scan();
        if (dc == null || traderId == null || !_acquaint.TryGetValue(traderId, out var vars) || vars.Count == 0) return;
        var profile = ((IDialogContext)dc).Profile;
        if (profile == null) return;
        foreach (var v in vars)
        {
            if (profile.ProfileVariables.GetVariableValue(v) != 0) continue;
            if (WrittenByLoadedDialog(v)) continue;
            dc.SetVariableValue(new DialogSetVariableAction.SaveStateData(v, 1, DialogLineTemplate.ESaveStateType.Profile));
            Vars.Sync(v, 1);
        }
    }

    // 已加载对话里所有 SetVariable 的目标 → 第一个写它的对话。模板表没变就复用（表实例 + 条数 + 各模板对象身份一起比，热重载替换模板也能察觉）
    static Dictionary<MongoID, MongoID> _writers;
    static object _writersTable;
    static int _writersCount;
    static long _writersStamp;

    static Dictionary<MongoID, MongoID> Writers()
    {
        var templates = DialogStorage.Instance?._dialogTemplates;
        if (templates == null) return null;
        long stamp = 0;
        foreach (var t in templates.Values) if (t != null) stamp += RuntimeHelpers.GetHashCode(t);
        if (_writers != null && ReferenceEquals(_writersTable, templates) && _writersCount == templates.Count && _writersStamp == stamp) return _writers;
        var writers = new Dictionary<MongoID, MongoID>();
        foreach (var t in templates.Values)
            if (t?.Lines != null)
                foreach (var line in t.Lines)
                    if (line?.Actions != null)
                        foreach (var a in line.Actions)
                            if (a is DialogSetVariableAction set && !writers.ContainsKey(set.Variable.VariableId))
                                writers[set.Variable.VariableId] = t.Id;
        _writers = writers;
        _writersTable = templates;
        _writersCount = templates.Count;
        _writersStamp = stamp;
        return writers;
    }

    static bool WrittenByLoadedDialog(MongoID v)
    {
        var writers = Writers();
        return writers != null && writers.ContainsKey(v);
    }

    /// <summary>零售对话表的客户端副本，和房间文件住一起（rooms\，老目录见 VisitPaths）。</summary>
    static string DialogueJsonPath => Path.Combine(VisitPaths.Rooms, "dialogue.json");

    /// 按元素一个一个读（JsonTextReader + JObject.Load），同一时刻只有一个元素在内存里：整份 JObject.Parse 这 12 MB 瞬时在堆上留几百 MB 的 JToken，
    /// 第一次开对话卡一下、再来一波 GC。开游戏时 Prefetch 已经在后台跑过，正常情况这里直接返回
    static void Scan()
    {
        lock (ScanGate)
        {
            if (_scanned) return;
            _scanned = true;
            var path = DialogueJsonPath;
            if (!File.Exists(path)) { Plugin.Log.LogWarning("[retail] rooms\\dialogue.json not found; no variable seeding / acquaintance marking for visit dialogues: " + path); return; }
            try
            {
                var entries = new Dictionary<string, MongoID>(StringComparer.OrdinalIgnoreCase);
                var seeds = new List<MongoID>();
                var acquaint = new Dictionary<string, List<MongoID>>(StringComparer.OrdinalIgnoreCase);
                ScanFile(path, entries, seeds, acquaint);
                _entries = entries;
                _seeds = seeds;
                _acquaint = acquaint;
            }
            catch (Exception e) { Plugin.Log.LogError("[retail] rooms\\dialogue.json parse failed; no variable seeding / acquaintance marking for visit dialogues: " + e.Message); }
        }
    }

    /// 09-24 审查低项：只补对话表里没有的模板和它们的文案。整份 AddTemplates + UpdateLocales 的话，
    /// 会用 rooms\dialogue.json 这份旧副本把服务端下发的新版对话结构和译文在本局里换回去
    static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        Scan();
        var path = DialogueJsonPath;
        if (!File.Exists(path)) return;
        TraderDialogsDTO dto;
        try
        {
            var settings = new JsonSerializerSettings { Error = (_, e) => e.ErrorContext.Handled = true, Converters = { new NestedLocaleConverter() } };
            dto = JsonConvert.DeserializeObject<TraderDialogsDTO>(File.ReadAllText(path), settings);
        }
        catch (Exception e) { dto = null; Plugin.Log.LogWarning("[retail] dialogue.json read failed: " + e.Message); }
        if (dto?.Elements == null)
        {
            _loaded = false;
            Plugin.Log.LogWarning("[retail] dialogue.json parse failed");
            return;
        }
        var storage = DialogStorage.Instance;
        var missing = dto.Elements.Where(e => e != null && !storage.TryGetTemplate(e.Id, out _)).ToList();
        var locales = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in missing.Where(e => e.LocalizationDictionary != null))
            foreach (var loc in t.LocalizationDictionary)
            {
                if (loc.Value == null) continue;
                if (!locales.TryGetValue(loc.Key, out var merged)) merged = locales[loc.Key] = new Dictionary<string, string>();
                foreach (var kv in loc.Value) merged[kv.Key] = kv.Value;
            }
        storage.AddTemplates(missing);
        if (LocalizationManager.Instance != null)
            foreach (var loc in locales) LocalizationManager.Instance.UpdateLocales(loc.Key, loc.Value);
        foreach (var id in _entries.Values)
            if (storage.TryGetTemplate(id, out var t)) t.CanBeFirstDialog = true;
    }

    /// 流式走到 "elements" 数组，每个元素单独 Load 成 JObject、处理完就丢
    static void ScanFile(string path, Dictionary<string, MongoID> entries, List<MongoID> seedList, Dictionary<string, List<MongoID>> acquaint)
    {
        var written = new HashSet<string>();
        var used = new HashSet<string>();
        var usedBy = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        using (var file = File.OpenText(path))
        using (var reader = new JsonTextReader(file))
        {
            while (reader.Read())
            {
                if (reader.TokenType != JsonToken.PropertyName || reader.Depth != 1 || (string)reader.Value != "elements") continue;
                if (!reader.Read() || reader.TokenType != JsonToken.StartArray) break;
                while (reader.Read() && reader.TokenType != JsonToken.EndArray)
                {
                    if (reader.TokenType != JsonToken.StartObject) { reader.Skip(); continue; }
                    ScanElement(JObject.Load(reader), entries, written, used, usedBy);
                }
                break;
            }
        }
        var seeds = new HashSet<string>(used.Except(written));
        foreach (var v in seeds) seedList.Add(new MongoID(v));
        foreach (var (trader, vars) in usedBy)
            acquaint[trader] = vars.Where(seeds.Contains).Where(KnownSeeds.ContainsKey).Select(v => new MongoID(v)).ToList();
    }

    static void ScanElement(JObject el, Dictionary<string, MongoID> entries, HashSet<string> written, HashSet<string> used, Dictionary<string, HashSet<string>> usedBy)
    {
        var trader = el.Value<string>("Trader");
        if (el.Value<bool?>("IsStart") == true)
        {
            var id = el.Value<string>("Id");
            if (trader != null && id != null && !entries.ContainsKey(trader)) entries[trader] = new MongoID(id);
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
