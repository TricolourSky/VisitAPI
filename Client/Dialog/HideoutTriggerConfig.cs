using System.Collections.Generic;
using Newtonsoft.Json;

namespace VisitAPI;

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

	// Empty = resolved to a localized default ("拜访"/"Visit") at spawn time; a `trigger: ... "prompt"` overrides it.
	[JsonProperty("promptText")]
	public string PromptText { get; set; } = "";

	[JsonProperty("maxDistance")]
	public float MaxDistance { get; set; } = 3f;

	[JsonProperty("offset")]
	public float[]? Offset { get; set; }

	[JsonProperty("questId")]
	public string? QuestId { get; set; }

	[JsonProperty("showWhenStatus")]
	public List<string>? ShowWhenStatus { get; set; }

	// Free-standing trigger: the spot has no native interaction object to merge into (e.g. open floor next to the
	// gym), so show the prompt by LOOKING at the point and replace the interaction state — like the raid `door`.
	[JsonProperty("freeStanding")]
	public bool FreeStanding { get; set; }

	[JsonProperty("hitRadius")]
	public float HitRadius { get; set; } = 1.2f;
}

internal sealed class HideoutTriggerConfig
{
	[JsonProperty("triggers")]
	public List<HideoutAreaTrigger> Triggers { get; set; } = new List<HideoutAreaTrigger>();
}
