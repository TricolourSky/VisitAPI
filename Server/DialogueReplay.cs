using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI.Routing;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Inventory;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace VisitAPI.Server;

[Injectable(InjectionType.Transient, 300000)]
public sealed class DialogueReplayRouter : ItemEventRouter
{
	// 首个请求时快照 templates.Dialogue 建索引且永不失效——依赖所有对话加载器都在 OnLoad 阶段完成灌入
	private static Dictionary<string, TraderDialogElement> _index;

	public DialogueReplayRouter(ProfileHelper profiles, TemplateTable templates, ISptLogger<DialogueReplayRouter> log)
		: base(new ItemRouteAction[1]
		{
			new ItemRouteAction<SaveDialogueStateRequest>("SaveDialogueState", delegate(string url, PmcData pmc, SaveDialogueStateRequest body, MongoId sessionId, ItemEventRouterResponse output, CancellationToken ct)
			{
				try
				{
					Replay(profiles, templates, log, pmc, body, sessionId);
				}
				catch (Exception ex)
				{
					log.Error("[VisitAPI] dialogue replay failed (ignored): " + ex.Message);
				}
				return new ValueTask<ItemEventRouterResponse>(output);
			})
		})
	{
	}

	private static void Replay(ProfileHelper profiles, TemplateTable templates, ISptLogger<DialogueReplayRouter> log, PmcData pmc, SaveDialogueStateRequest body, MongoId sessionId)
	{
		profiles.GetFullProfile(sessionId).DialogueProgress = body.DialogueProgress;
		if (body.DialogueProgress == null || body.DialogueProgress.Count == 0 || pmc == null)
		{
			return;
		}
		if (pmc.Variables == null)
		{
			pmc.Variables = new Dictionary<MongoId, int>();
		}
		if (_index == null)
		{
			_index = (from e in templates.Dialogue.Elements
				group e by e.Id.ToString()).ToDictionary((IGrouping<string, TraderDialogElement> g) => g.Key, (IGrouping<string, TraderDialogElement> g) => g.First());
		}
		int persisted = 0;
		foreach (NodePathTraveled step in body.DialogueProgress)
		{
			if (step != null && step.DialogueId != null && step.NodeId != null && _index.TryGetValue(step.DialogueId, out var element))
			{
				persisted += Apply(element, step.NodeId, pmc.Variables);
			}
		}
		if (persisted > 0)
		{
			log.Debug($"[VisitAPI] dialogue replay: {persisted} profile variable(s) persisted");
		}
	}

	private static int Apply(TraderDialogElement element, string nodeId, Dictionary<MongoId, int> variables)
	{
		if (element.Lines == null)
		{
			return 0;
		}
		foreach (object line in element.Lines)
		{
			if (!(line is JsonElement { ValueKind: JsonValueKind.Object } node) || !node.TryGetProperty("Id", out var idProp) || idProp.GetString() != nodeId)
			{
				continue;
			}
			if (!node.TryGetProperty("Actions", out var actionsProp) || actionsProp.ValueKind != JsonValueKind.Array)
			{
				return 0;
			}
			int count = 0;
			foreach (JsonElement action in actionsProp.EnumerateArray())
			{
				if (action.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "SetVariable"
					&& action.TryGetProperty("saveScope", out var scopeProp) && scopeProp.GetString() == "Profile"
					&& action.TryGetProperty("variableId", out var variableIdProp)
					&& action.TryGetProperty("value", out var valueProp) && valueProp.TryGetInt32(out var intValue))
				{
					string variableId = variableIdProp.GetString();
					// 24 = MongoId 长度; 非法 variableId 走隐式转换会抛异常, 提前丢弃
					if (variableId != null && variableId.Length == 24)
					{
						variables[variableId] = intValue;
						count++;
					}
				}
			}
			return count;
		}
		return 0;
	}
}
