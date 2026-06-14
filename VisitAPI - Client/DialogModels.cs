using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace VisitAPI;

// ── Data models (dialog tree JSON schema) ─────────────────────────────────────

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

	[JsonProperty("randomAfterRaid")]
	public RandomAfterRaid? RandomAfterRaid { get; set; }

	[JsonProperty("hideoutTriggers")]
	public List<HideoutAreaTrigger>? HideoutTriggers { get; set; }

	// 商人界面"拜访"页签门控：仅当指定任务处于这些状态时显示页签（不配置 = 始终显示）
	[JsonProperty("tabQuestId")]
	public string? TabQuestId { get; set; }

	[JsonProperty("tabShowWhenStatus")]
	[JsonConverter(typeof(StringOrListConverter))]
	public List<string>? TabShowWhenStatus { get; set; }

	[JsonProperty("nodes")]
	public Dictionary<string, DialogNode> Nodes { get; set; } = new Dictionary<string, DialogNode>();
}

internal sealed class DialogNode
{
	[JsonProperty("narration")]
	public List<string>? Narration { get; set; }

	[JsonProperty("npcText")]
	[JsonConverter(typeof(StringOrListConverter))]
	public List<string>? NpcTextLines { get; set; }

	[JsonIgnore]
	public string NpcText
	{
		get
		{
			List<string> lines = NpcTextLines;
			if (lines == null || lines.Count == 0) return "";
			return lines[lines.Count - 1];
		}
	}

	[JsonProperty("background")]
	public string? Background { get; set; }

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

	[JsonProperty("acceptQuestId")]
	public string? AcceptQuestId { get; set; }

	[JsonProperty("showWhenStatus")]
	[JsonConverter(typeof(StringOrListConverter))]
	public List<string>? ShowWhenStatus { get; set; }

	[JsonProperty("hideWhenStatus")]
	[JsonConverter(typeof(StringOrListConverter))]
	public List<string>? HideWhenStatus { get; set; }
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

	[JsonProperty("promptText")]
	public string PromptText { get; set; } = "拜访";

	[JsonProperty("doorWidth")]
	public float DoorWidth { get; set; }

	[JsonProperty("doorHeight")]
	public float DoorHeight { get; set; } = 2.2f;

	[JsonProperty("doorRotationY")]
	public float DoorRotationY { get; set; }
}

internal sealed class RandomAfterRaid
{
	[JsonProperty("chance")]
	public float Chance { get; set; } = 10f;

	[JsonProperty("nodes")]
	public List<string> Nodes { get; set; } = new List<string>();
}

// ── JSON converter ─────────────────────────────────────────────────────────────

internal sealed class StringOrListConverter : JsonConverter<List<string>?>
{
	public override List<string>? ReadJson(JsonReader reader, Type objectType, List<string>? existingValue, bool hasExistingValue, JsonSerializer serializer)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0009: Invalid comparison between Unknown and I4
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Invalid comparison between Unknown and I4
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_007a: Invalid comparison between Unknown and I4
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Invalid comparison between Unknown and I4
		JsonToken tokenType = reader.TokenType;
		if ((int)tokenType != 2)
		{
			if ((int)tokenType != 9)
			{
				_ = 11;
				return null;
			}
			string text = reader.Value as string;
			if (!string.IsNullOrEmpty(text))
				return new List<string> { text };
			return null;
		}
		List<string> list = new List<string>();
		while (reader.Read() && (int)reader.TokenType != 14)
		{
			if ((int)reader.TokenType == 9 && reader.Value is string text2 && !string.IsNullOrEmpty(text2))
				list.Add(text2);
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
