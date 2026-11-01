using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils;
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
        var native = templates.Dialogue.Elements.Select(e => e.Id.ToString()).ToHashSet();   // 原生对话的 id：Register 用它判重
        var known = new HashSet<string>(native);                                            // 再加上包里的：判断跳转断没断
        var parsed = Prepare(pending, known, texts);
        Register(pending, parsed, known, native);
        foreach (var (localeId, table) in texts)
            if (table.Count > 0 && locales.Global.TryGetValue(localeId, out var lazy)) LocaleText.Add(lazy, table);
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
        return pending;
    }

    /// 整理每个元素并试解析一次（拿 id、坏的当场报）；解析结果按 pending 的顺序留给 Register，解析不了的位置是 null
    List<TraderDialogElement> Prepare(List<(string file, JsonObject element)> pending, HashSet<string> known, Dictionary<string, Dictionary<string, string>> texts)
    {
        var result = new List<TraderDialogElement>(pending.Count);
        foreach (var (fileName, element) in pending)
        {
            foreach (var key in new[] { "StartPoints", "localization" }) if (element[key] is not JsonObject) element[key] = new JsonObject();
            if (element["SubTraders"] is not JsonArray) element["SubTraders"] = new JsonArray();
            CollectTexts((JsonObject)element["localization"]!, texts);
            DialogueConfirmations.Scan(element);
            DialogueSanitizer.Clean(element);
            DialogueSanitizer.SplitRandomGroups(element);
            DialogueSanitizer.HalfOpenRandoms(element);
            TraderDialogElement parsed = null;
            try { parsed = json.Deserialize<TraderDialogElement>(element.ToJsonString())!; known.Add(parsed.Id.ToString()); }
            catch (Exception ex) { log.Error($"[VisitAPI] bad element {element["Id"]} in {fileName}: {ex.Message}"); }
            result.Add(parsed);
        }
        return result;
    }

    /// 断掉的跳转改成退出；改动过的元素才重新解析，没动过的直接用 Prepare 那份（同一段 JSON，结果一样）
    void Register(List<(string file, JsonObject element)> pending, List<TraderDialogElement> prepared, HashSet<string> known, HashSet<string> existing)
    {
        var owner = new Dictionary<string, string>();   // 元素 id → 包/文件：两个包都有同一个元素时点名、先来的赢；和原生同 id 的照旧静默跳过
        for (var i = 0; i < pending.Count; i++)
        {
            var (fileName, element) = pending[i];
            var parsed = prepared[i];
            if (DialogueSanitizer.FixDanglingSwitches(element, known) > 0)
            {
                try { parsed = json.Deserialize<TraderDialogElement>(element.ToJsonString())!; }
                catch { continue; }
            }
            if (parsed == null) continue;
            var id = parsed.Id.ToString();
            if (!existing.Add(id))
            {
                if (owner.TryGetValue(id, out var first)) log.Error($"[VisitAPI] Dialogue element {id} exists in both {first} and {fileName}, using the former");
                continue;
            }
            owner[id] = fileName;
            templates.Dialogue.Elements.Add(parsed);
            var trader = traders.GetTrader(parsed.MainTrader)?.Base;
            var isStart = element["IsStart"] is JsonValue startNode && startNode.TryGetValue(out bool start) && start;
            if (isStart && trader != null && trader.MainDialogue == null) trader.MainDialogue = id;
        }
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
