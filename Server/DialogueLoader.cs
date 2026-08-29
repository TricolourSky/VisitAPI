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

[Injectable(InjectionType.Transient, 1000000)]
public class DialogueLoader(TemplateTable templates, TradersTable traders, LocaleTable locales, JsonUtil json, ISptLogger<DialogueLoader> log) : IOnLoad
{
	public Task OnLoadAsync(CancellationToken cancellationToken)
	{
		string dir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "db", "dialogues");
		if (!Directory.Exists(dir))
		{
			return Task.CompletedTask;
		}
		HashSet<string> existingIds = templates.Dialogue.Elements.Select((TraderDialogElement e) => e.Id.ToString()).ToHashSet();
		Dictionary<string, Dictionary<string, string>> localeTexts = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
		List<(string File, JsonObject Element)> pending = new List<(string, JsonObject)>();
		foreach (string file in Directory.GetFiles(dir, "*.json"))
		{
			JsonArray elements;
			try
			{
				elements = (JsonNode.Parse(File.ReadAllText(file))?["elements"] as JsonArray) ?? new JsonArray();
			}
			catch (Exception ex)
			{
				log.Error("[VisitAPI] cannot parse " + Path.GetFileName(file) + ": " + ex.Message);
				continue;
			}
			foreach (JsonObject element in elements.OfType<JsonObject>())
			{
				pending.Add((Path.GetFileName(file), element));
			}
		}
		// 悬空跳转判定需要"全部文件收齐后"的完整 id 集合, 所以先收集再逐个修复
		HashSet<string> knownIds = new HashSet<string>(existingIds);
		foreach (var (_, element) in pending)
		{
			if (element["Id"] is JsonValue idNode && idNode.TryGetValue<string>(out string id))
			{
				knownIds.Add(id);
			}
		}
		int added = 0, entriesSet = 0, linesDropped = 0, switchesFixed = 0;
		foreach (var (fileName, element) in pending)
		{
			if (!(element["StartPoints"] is JsonObject))
			{
				element["StartPoints"] = new JsonObject();
			}
			if (!(element["localization"] is JsonObject))
			{
				element["localization"] = new JsonObject();
			}
			if (!(element["SubTraders"] is JsonArray))
			{
				element["SubTraders"] = new JsonArray();
			}
			CollectTexts((JsonObject)element["localization"], localeTexts);
			linesDropped += DialogueSanitizer.Clean(element);
			switchesFixed += DialogueSanitizer.FixDanglingSwitches(element, knownIds);
			TraderDialogElement parsed;
			try
			{
				parsed = json.Deserialize<TraderDialogElement>(element.ToJsonString());
			}
			catch (Exception ex)
			{
				log.Error($"[VisitAPI] bad element {element["Id"]} in {fileName}: {ex.Message}");
				continue;
			}
			if (!existingIds.Add(parsed.Id.ToString()))
			{
				continue;
			}
			templates.Dialogue.Elements.Add(parsed);
			added++;
			TraderBase trader = traders.GetTrader(parsed.MainTrader)?.Base;
			bool isStart = element["IsStart"] is JsonValue startNode && startNode.TryGetValue(out bool start) && start;
			if (isStart && trader != null && trader.MainDialogue == null)
			{
				trader.MainDialogue = parsed.Id.ToString();
				entriesSet++;
			}
		}
		int localesMerged = 0;
		foreach (var (localeId, table) in localeTexts)
		{
			if (table.Count == 0 || !locales.Global.TryGetValue(localeId, out LazyLoad<GlobalLocaleDictionary> lazy))
			{
				continue;
			}
			lazy.AddTransformer(delegate(GlobalLocaleDictionary data)
			{
				if (data == null)
				{
					return data;
				}
				foreach (var (textKey, text) in table)
				{
					data.TryAdd(textKey, text);
				}
				return data;
			});
			localesMerged += table.Count;
		}
		if (added > 0)
		{
			log.Debug($"[VisitAPI] native dialogue pipeline: +{added} element(s), {entriesSet} trader entry(ies) set, {linesDropped} line(s) dropped (client-incompatible), {switchesFixed} dangling switch(es) turned into quit, {localesMerged} locale entr(ies) merged");
		}
		return Task.CompletedTask;
	}

	private static void CollectTexts(JsonObject localization, Dictionary<string, Dictionary<string, string>> texts)
	{
		foreach (var (localeId, node) in localization)
		{
			if (!(node is JsonObject entries))
			{
				continue;
			}
			if (!texts.TryGetValue(localeId, out Dictionary<string, string> table))
			{
				table = texts[localeId] = new Dictionary<string, string>();
			}
			foreach (var (textKey, textNode) in entries)
			{
				if (textNode is JsonValue value && value.TryGetValue<string>(out string text))
				{
					table[textKey] = text;
				}
			}
		}
	}
}
