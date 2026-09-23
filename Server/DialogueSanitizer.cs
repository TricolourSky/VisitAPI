using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace VisitAPI.Server;

public static class DialogueSanitizer
{
	private static readonly HashSet<string> Conditions = new HashSet<string> { "VariableValue", "QuestStatus", "TraderReputation", "QuestConditionStatus", "HasNewQuests", "MainLogicalGroup", "LogicalSubGroup", "HasItemForHandover", "ServiceAvailable", "CurrentTrader" };

	private static readonly HashSet<string> Actions = new HashSet<string>
	{
		"SetVariable", "DiaryNote", "SwitchDialog", "QuitAction", "TradingScreenAction", "QuestsScreenAction", "SwitchQuestDialog", "SelectQuest", "AcceptQuest", "HandoverItem",
		"FinishQuest", "PlayerReward", "SelectSubService", "PurchaseService"
	};

	private const string ConstantVariable = "000000000000000000000000";
	public static int Converted;

	private static int Convert(JsonNode node)
	{
		if (node is JsonArray array)
		{
			return array.Sum(Convert);
		}
		if (!(node is JsonObject obj))
		{
			return 0;
		}
		int count = Convert(obj["Conditions"]);
		if (!(obj["type"] is JsonValue typeValue) || !typeValue.TryGetValue<string>(out string type))
		{
			return count;
		}
		bool? truth = type switch
		{
			"CompletableItem" => !(obj["isCompleted"] is JsonValue c && c.TryGetValue<bool>(out bool done) && done),
			"HasFreeSpecialSlot" => obj["hasFreeSlot"] is JsonValue f && f.TryGetValue<bool>(out bool free) && free,
			_ => null
		};
		if (truth == null)
		{
			return count;
		}
		foreach (string key in obj.Select(kv => kv.Key).Where(k => k != "id" && k != "Id").ToList())
		{
			obj.Remove(key);
		}
		obj["type"] = "VariableValue";
		obj["variableId"] = ConstantVariable;
		obj["value"] = truth.Value ? 0 : 1;
		obj["operator"] = "==";
		return count + 1;
	}

	public static int Clean(JsonObject element)
	{
		if (!(element["Lines"] is JsonArray lines))
		{
			return 0;
		}
		foreach (JsonObject line in lines.OfType<JsonObject>())
		{
			Converted += Convert(line["Trigger"]);
		}
		List<JsonObject> removed = (from line in lines.OfType<JsonObject>()
			where Unsupported(line["Trigger"], Conditions) || Unsupported(line["Actions"], Actions)
			select line).ToList();
		foreach (JsonObject line in removed)
		{
			lines.Remove(line);
		}
		return removed.Count;
	}

	public static int FixDanglingSwitches(JsonObject element, HashSet<string> knownIds)
	{
		if (!(element["Lines"] is JsonArray lines))
		{
			return 0;
		}
		int count = 0;
		foreach (JsonObject line in lines.OfType<JsonObject>())
		{
			if (!(line["Actions"] is JsonArray actions))
			{
				continue;
			}
			foreach (JsonObject action in actions.OfType<JsonObject>())
			{
				if (action["type"] is JsonValue typeValue && typeValue.TryGetValue<string>(out string type) && type == "SwitchDialog"
					&& action["dialogId"] is JsonValue idValue && idValue.TryGetValue<string>(out string target) && !knownIds.Contains(target))
				{
					action["type"] = "QuitAction";
					action.Remove("dialogId");
					action.Remove("splitterNodeId");
					count++;
				}
			}
		}
		return count;
	}

	private static bool Unsupported(JsonNode node, HashSet<string> allowed)
	{
		if (node is JsonArray array)
		{
			return array.Any((JsonNode item) => Unsupported(item, allowed));
		}
		if (!(node is JsonObject obj))
		{
			return false;
		}
		if (obj["type"] is JsonValue typeNode && typeNode.TryGetValue<string>(out string type) && !allowed.Contains(type))
		{
			return true;
		}
		return Unsupported(obj["Conditions"], allowed);
	}
}
