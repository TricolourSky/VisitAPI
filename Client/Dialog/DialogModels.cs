using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace VisitAPI;

internal sealed class DialogTree
{
	[JsonProperty("traderId")]
	public string TraderId { get; set; } = "";

	[JsonProperty("traderName")]
	public string TraderName { get; set; } = "";

	[JsonProperty("startNode")]
	public string StartNode { get; set; } = "root";

	[JsonProperty("firstVisitNode")]
	public string? FirstVisitNode { get; set; }

	[JsonProperty("nodeConditions")]
	public List<NodeCondition>? NodeConditions { get; set; }

	[JsonProperty("firstVisitTrigger")]
	public FirstVisitTrigger? FirstVisitTrigger { get; set; }

	[JsonProperty("raidTriggers")]
	public List<FirstVisitTrigger>? RaidTriggers { get; set; }

	public IEnumerable<FirstVisitTrigger> AllRaidTriggers()
	{
		if (FirstVisitTrigger != null)
		{
			yield return FirstVisitTrigger;
		}
		if (RaidTriggers != null)
		{
			foreach (FirstVisitTrigger t in RaidTriggers)
			{
				if (t != null)
				{
					yield return t;
				}
			}
		}
	}

	[JsonProperty("randomAfterRaid")]
	public RandomAfterRaid? RandomAfterRaid { get; set; }

	[JsonProperty("hideoutTriggers")]
	public List<HideoutAreaTrigger>? HideoutTriggers { get; set; }

	[JsonProperty("tabQuestId")]
	public string? TabQuestId { get; set; }

	[JsonProperty("tabShowWhenStatus")]
	[JsonConverter(typeof(StringOrListConverter))]
	public List<string>? TabShowWhenStatus { get; set; }

	[JsonProperty("tabAlways")]
	public bool TabAlways { get; set; }

	[JsonProperty("nodes")]
	public Dictionary<string, DialogNode> Nodes { get; set; } = new Dictionary<string, DialogNode>();
}

internal sealed class DialogNode
{
	[JsonProperty("narration")]
	public List<string>? Narration { get; set; }

	// Per-narration-line background, index-aligned with Narration (null = keep the current background). Set by a
	// trailing `| bg: <file>` on a `>` line, so a single node can switch backgrounds between narration lines.
	[JsonProperty("narrationBg")]
	public List<string?>? NarrationBackgrounds { get; set; }

	[JsonProperty("npcText")]
	[JsonConverter(typeof(StringOrListConverter))]
	public List<string>? NpcTextLines { get; set; }

	[JsonIgnore]
	public string NpcText
	{
		get
		{
			List<string>? lines = NpcTextLines;
			if (lines == null || lines.Count == 0) return "";
			return lines[lines.Count - 1];
		}
	}

	[JsonProperty("background")]
	public string? Background { get; set; }

	// Node-level auto-continue target for a pure-narration node (a `-> X` line with no option text). When the
	// node has no clickable options, the runner shows a single "continue" row that jumps here (or closes if null).
	[JsonProperty("jumpTo")]
	public string? JumpTo { get; set; }

	[JsonProperty("options")]
	public List<DialogOption> Options { get; set; } = new List<DialogOption>();
}

internal sealed class DialogOption
{
	[JsonProperty("text")]
	public string Text { get; set; } = "";

	[JsonProperty("next")]
	public string? Next { get; set; }

	[JsonProperty("action")]
	public string? Action { get; set; }

	[JsonProperty("once")]
	public bool Once { get; set; }

	[JsonProperty("questId")]
	public string? QuestId { get; set; }

	// Optional display label for a `handover:` window. The native HandoverQuestItemsWindow titles itself from the
	// FIRST eligible item's short name (e.g. "螺栓"); with a multi-tpl "category" hand-over that's misleading, so the
	// author can write `handover: <id> 建筑材料` to show that instead.
	[JsonProperty("handoverLabel")]
	public string? HandoverLabel { get; set; }

	[JsonProperty("acceptQuestId")]
	public string? AcceptQuestId { get; set; }

	[JsonProperty("showWhenStatus")]
	[JsonConverter(typeof(StringOrListConverter))]
	public List<string>? ShowWhenStatus { get; set; }

	[JsonProperty("hideWhenStatus")]
	[JsonConverter(typeof(StringOrListConverter))]
	public List<string>? HideWhenStatus { get; set; }

	[JsonProperty("standingDelta")]
	public double? StandingDelta { get; set; }

	[JsonProperty("standingTarget")]
	public string? StandingTarget { get; set; }

	[JsonProperty("setStatusQuestId")]
	public string? SetStatusQuestId { get; set; }

	[JsonProperty("setStatusValue")]
	public int? SetStatusValue { get; set; }
}

internal sealed class NodeCondition
{
	[JsonProperty("minLevel")]
	public int MinLevel { get; set; }

	[JsonProperty("maxLevel")]
	public int MaxLevel { get; set; } = 99;

	[JsonProperty("minStanding")]
	public double MinStanding { get; set; } = double.MinValue;

	[JsonProperty("maxStanding")]
	public double MaxStanding { get; set; } = double.MaxValue;

	[JsonProperty("node")]
	public string Node { get; set; } = "";
}

internal sealed class FirstVisitTrigger
{
	[JsonProperty("type")]
	public string Type { get; set; } = "interact";

	[JsonProperty("map")]
	public string Map { get; set; } = "*";

	[JsonProperty("position")]
	public float[] Position { get; set; } = Array.Empty<float>();

	[JsonProperty("maxDistance")]
	public float MaxDistance { get; set; } = 3f;

	[JsonProperty("hitRadius")]
	public float HitRadius { get; set; } = 1.2f;

	// Empty = resolved to a localized default ("拜访"/"Visit") at spawn time; a `trigger: ... "prompt"` overrides it.
	[JsonProperty("promptText")]
	public string PromptText { get; set; } = "";

	[JsonProperty("doorWidth")]
	public float DoorWidth { get; set; }

	[JsonProperty("doorHeight")]
	public float DoorHeight { get; set; } = 2.2f;

	[JsonProperty("doorRotationY")]
	public float DoorRotationY { get; set; }

	[JsonProperty("once")]
	public bool Once { get; set; } = true;
}

internal sealed class RandomAfterRaid
{
	[JsonProperty("chance")]
	public float Chance { get; set; } = 10f;

	[JsonProperty("nodes")]
	public List<string> Nodes { get; set; } = new List<string>();
}

internal sealed class StringOrListConverter : JsonConverter<List<string>?>
{
	public override List<string>? ReadJson(JsonReader reader, Type objectType, List<string>? existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		if (reader.TokenType == JsonToken.String)
		{
			string? text = reader.Value as string;
			return string.IsNullOrEmpty(text) ? null : new List<string> { text };
		}
		if (reader.TokenType != JsonToken.StartArray)
		{
			return null;
		}
		List<string> list = new List<string>();
		while (reader.Read() && reader.TokenType != JsonToken.EndArray)
		{
			if (reader.TokenType == JsonToken.String && reader.Value is string text2 && !string.IsNullOrEmpty(text2))
			{
				list.Add(text2);
			}
		}
		return list.Count > 0 ? list : null;
	}

	public override void WriteJson(JsonWriter writer, List<string>? value, JsonSerializer serializer)
	{
		if (value == null || value.Count == 0)
			writer.WriteNull();
		else if (value.Count == 1)
			writer.WriteValue(value[0]);
		else
			serializer.Serialize(writer, (object)value);
	}
}
