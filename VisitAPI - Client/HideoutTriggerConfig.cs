using System.Collections.Generic;
using Newtonsoft.Json;

namespace VisitAPI;

// ── Hideout trigger config (JSON schema) ──────────────────────────────────────

internal sealed class HideoutAreaTrigger
{
	[JsonProperty("areaType")]
	public string AreaType { get; set; } = "";

	[JsonProperty("requiredLevel")]
	public int RequiredLevel { get; set; } = 1;

	[JsonProperty("traderId")]
	public string TraderId { get; set; } = "";

	[JsonProperty("node")]
	public string? Node { get; set; }

	[JsonProperty("promptText")]
	public string PromptText { get; set; } = "拜访";

	[JsonProperty("maxDistance")]
	public float MaxDistance { get; set; } = 3f;

	[JsonProperty("offset")]
	public float[]? Offset { get; set; }

	[JsonProperty("questId")]
	public string? QuestId { get; set; }

	[JsonProperty("showWhenStatus")]
	public List<string>? ShowWhenStatus { get; set; }
}

internal sealed class HideoutTriggerConfig
{
	[JsonProperty("triggers")]
	public List<HideoutAreaTrigger> Triggers { get; set; } = new List<HideoutAreaTrigger>();
}
