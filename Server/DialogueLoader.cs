using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;
using Path = System.IO.Path;

namespace VisitAPI.Server;

/// <summary>1.0 零售 `db\dialogues\*.json` 灌进 SPT 的对话模板表（原生 narrate 路径的台词数据源）：
/// 收齐全部元素 → 补齐客户端要求的字段、剔掉 0.16 不认的行、悬空跳转改成关闭 → 逐个反序列化登记，
/// 首个 IsStart 元素设为商人主对话；文案合并进全局本地化表（LazyLoad 变换器，和 CustomQuestService 同一条路）。</summary>
[Injectable(InjectionType.Transient, 1000000)]
public class DialogueLoader(TemplateTable templates, TradersTable traders, LocaleTable locales, JsonUtil json, ISptLogger<DialogueLoader> log) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var dir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "db", "dialogues");
        if (!Directory.Exists(dir)) return Task.CompletedTask;
        var pending = Collect(dir);
        // 悬空跳转判定需要"全部文件收齐后"的完整 id 集合，所以先收集再逐个修复
        var known = templates.Dialogue.Elements.Select(e => e.Id.ToString()).ToHashSet();
        foreach (var (_, element) in pending)
            if (element["Id"] is JsonValue idNode && idNode.TryGetValue<string>(out var id)) known.Add(id);
        var texts = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var stats = Register(pending, known, texts);
        var merged = MergeLocales(texts);
        if (stats.added > 0)
            log.Debug($"[VisitAPI] native dialogue pipeline: +{stats.added} element(s), {stats.entries} trader entry(ies) set, {stats.dropped} line(s) dropped (client-incompatible), {stats.fixedSwitches} dangling switch(es) turned into quit, {merged} locale entr(ies) merged");
        return Task.CompletedTask;
    }

    /// 目录里所有文件的 elements，解析失败的文件整个跳过并报错
    List<(string file, JsonObject element)> Collect(string dir)
    {
        var pending = new List<(string, JsonObject)>();
        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            JsonArray elements;
            try { elements = JsonNode.Parse(File.ReadAllText(file))?["elements"] as JsonArray ?? new JsonArray(); }
            catch (Exception ex) { log.Error("[VisitAPI] cannot parse " + Path.GetFileName(file) + ": " + ex.Message); continue; }
            pending.AddRange(elements.OfType<JsonObject>().Select(e => (Path.GetFileName(file), e)));
        }
        return pending;
    }

    /// 补字段 → 清洗 → 反序列化 → 登记；已存在的 id 不重复登记；IsStart 的元素填成商人主对话（商人还没有的话）
    (int added, int entries, int dropped, int fixedSwitches) Register(List<(string file, JsonObject element)> pending, HashSet<string> known, Dictionary<string, Dictionary<string, string>> texts)
    {
        var existing = templates.Dialogue.Elements.Select(e => e.Id.ToString()).ToHashSet();
        int added = 0, entries = 0, dropped = 0, fixedSwitches = 0;
        foreach (var (fileName, element) in pending)
        {
            foreach (var key in new[] { "StartPoints", "localization" }) if (element[key] is not JsonObject) element[key] = new JsonObject();
            if (element["SubTraders"] is not JsonArray) element["SubTraders"] = new JsonArray();
            CollectTexts((JsonObject)element["localization"]!, texts);
            dropped += DialogueSanitizer.Clean(element);
            fixedSwitches += DialogueSanitizer.FixDanglingSwitches(element, known);
            TraderDialogElement parsed;
            try { parsed = json.Deserialize<TraderDialogElement>(element.ToJsonString())!; }
            catch (Exception ex) { log.Error($"[VisitAPI] bad element {element["Id"]} in {fileName}: {ex.Message}"); continue; }
            if (!existing.Add(parsed.Id.ToString())) continue;
            templates.Dialogue.Elements.Add(parsed);
            added++;
            var trader = traders.GetTrader(parsed.MainTrader)?.Base;
            var isStart = element["IsStart"] is JsonValue startNode && startNode.TryGetValue(out bool start) && start;
            if (isStart && trader != null && trader.MainDialogue == null) { trader.MainDialogue = parsed.Id.ToString(); entries++; }
        }
        return (added, entries, dropped, fixedSwitches);
    }

    /// 文案按语言合并进全局表（只补缺，不覆盖已有键）；返回合并条数
    int MergeLocales(Dictionary<string, Dictionary<string, string>> texts)
    {
        var merged = 0;
        foreach (var (localeId, table) in texts)
        {
            if (table.Count == 0 || !locales.Global.TryGetValue(localeId, out var lazy)) continue;
            lazy.AddTransformer(data => { if (data != null) foreach (var (k, v) in table) data.TryAdd(k, v); return data; });
            merged += table.Count;
        }
        return merged;
    }

    static void CollectTexts(JsonObject localization, Dictionary<string, Dictionary<string, string>> texts)
    {
        foreach (var (localeId, node) in localization)
        {
            if (node is not JsonObject entries) continue;
            if (!texts.TryGetValue(localeId, out var table)) table = texts[localeId] = new Dictionary<string, string>();
            foreach (var (textKey, textNode) in entries)
                if (textNode is JsonValue value && value.TryGetValue<string>(out var text)) table[textKey] = text;
        }
    }
}
