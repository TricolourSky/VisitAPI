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
using VisitAPI.Packs;
using Path = System.IO.Path;

namespace VisitAPI.Server;

[Injectable(InjectionType.Transient, 1000000)]
public class DialogueLoader(TemplateTable templates, TradersTable traders, LocaleTable locales, JsonUtil json, ISptLogger<DialogueLoader> log) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        // 内容包（09-23）：先把所有包的元素收齐再登记——跨包的跳转（主线包跳进零售对话）才不会被当成断链改成退出
        var pending = new List<(string file, JsonObject element)>();
        foreach (var p in ContentPacks.All(log)) pending.AddRange(Collect(p));
        if (pending.Count == 0) return Task.CompletedTask;
        var texts = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var known = templates.Dialogue.Elements.Select(e => e.Id.ToString()).ToHashSet();
        Prepare(pending, known, texts);
        Register(pending, known);
        MergeLocales(texts);
        return Task.CompletedTask;
    }

    List<(string file, JsonObject element)> Collect(PackInfo p)
    {
        var pending = new List<(string, JsonObject)>();
        foreach (var file in PackLayout.DataFiles(p, "dialogues"))
        {
            var label = p.Name + "/" + Path.GetFileName(file);
            JsonArray elements;
            try { elements = JsonNode.Parse(File.ReadAllText(file))?["elements"] as JsonArray ?? new JsonArray(); }
            catch (Exception ex) { log.Error("[VisitAPI] cannot parse " + label + ": " + ex.Message); continue; }
            pending.AddRange(elements.OfType<JsonObject>().Select(e => (label, e)));
        }
        if (pending.Count > 0) log.Info($"[VisitAPI] 包 {p.Label}：对话元素 {pending.Count} 个");
        return pending;
    }

    int Prepare(List<(string file, JsonObject element)> pending, HashSet<string> known, Dictionary<string, Dictionary<string, string>> texts)
    {
        var dropped = 0;
        foreach (var (fileName, element) in pending)
        {
            foreach (var key in new[] { "StartPoints", "localization" }) if (element[key] is not JsonObject) element[key] = new JsonObject();
            if (element["SubTraders"] is not JsonArray) element["SubTraders"] = new JsonArray();
            CollectTexts((JsonObject)element["localization"]!, texts);
            DialogueConfirmations.Scan(element);
            dropped += DialogueSanitizer.Clean(element);
            try { var parsed = json.Deserialize<TraderDialogElement>(element.ToJsonString())!; known.Add(parsed.Id.ToString()); }
            catch (Exception ex) { log.Error($"[VisitAPI] bad element {element["Id"]} in {fileName}: {ex.Message}"); }
        }
        return dropped;
    }

    (int added, int entries, int fixedSwitches) Register(List<(string file, JsonObject element)> pending, HashSet<string> known)
    {
        var existing = templates.Dialogue.Elements.Select(e => e.Id.ToString()).ToHashSet();
        var owner = new Dictionary<string, string>();   // 元素 id → 包/文件：两个包都有同一个元素时点名、先来的赢；和原生同 id 的照旧静默跳过
        int added = 0, entries = 0, fixedSwitches = 0;
        foreach (var (fileName, element) in pending)
        {
            fixedSwitches += DialogueSanitizer.FixDanglingSwitches(element, known);
            TraderDialogElement parsed;
            try { parsed = json.Deserialize<TraderDialogElement>(element.ToJsonString())!; }
            catch { continue; }
            var id = parsed.Id.ToString();
            if (!existing.Add(id))
            {
                if (owner.TryGetValue(id, out var first)) log.Error($"[VisitAPI] 对话元素 {id} 在 {first} 和 {fileName} 里都有，用了前者的");
                continue;
            }
            owner[id] = fileName;
            templates.Dialogue.Elements.Add(parsed);
            added++;
            var trader = traders.GetTrader(parsed.MainTrader)?.Base;
            var isStart = element["IsStart"] is JsonValue startNode && startNode.TryGetValue(out bool start) && start;
            if (isStart && trader != null && trader.MainDialogue == null) { trader.MainDialogue = parsed.Id.ToString(); entries++; }
        }
        return (added, entries, fixedSwitches);
    }

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
