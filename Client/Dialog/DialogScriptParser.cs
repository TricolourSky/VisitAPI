using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace VisitAPI;

internal static class DialogScriptParser
{
	private static readonly Regex NodeHeaderRegex = new Regex(@"^<([A-Za-z0-9_.\-]+)>(?:\s+bg:\s*(.+))?$");

	private static readonly Regex QuestAliasRegex = new Regex(@"^quest\s+([A-Za-z0-9_\-]+)\s*=\s*(\S+)$");

	private static readonly Regex VectorRegex = new Regex(@"\(\s*(-?[\d.]+)\s*,\s*(-?[\d.]+)\s*,\s*(-?[\d.]+)\s*\)");

	private static readonly Regex QuotedRegex = new Regex("\"([^\"]*)\"");

	private static readonly Regex NarrationBgRegex = new Regex(@"\s*\|\s*bg:\s*(.+)$", RegexOptions.IgnoreCase);

	public static DialogTree? Parse(string[] lines, string sourceName, List<string> errors)
	{
		DialogTree tree = new DialogTree();
		Dictionary<string, string> questAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		DialogNode? node = null;
		for (int i = 0; i < lines.Length; i++)
		{
			string line = lines[i].Trim();
			if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//"))
			{
				continue;
			}
			Match header = NodeHeaderRegex.Match(line);
			if (header.Success)
			{
				node = new DialogNode();
				tree.Nodes[header.Groups[1].Value] = node;
				if (header.Groups[2].Success)
				{
					node.Background = NormalizeBackground(header.Groups[2].Value.Trim());
				}
				continue;
			}
			if (node == null)
			{
				ParseHeaderLine(line, tree, questAliases, sourceName, i + 1, errors);
				continue;
			}
			if (line.StartsWith("- "))
			{
				DialogOption? opt = ParseOption(line.Substring(2).Trim(), sourceName, i + 1, errors);
				if (opt != null)
				{
					node.Options.Add(opt);
				}
				continue;
			}
			if (line.StartsWith("->"))
			{
				// Node-level jump: a pure-narration node auto-continues to this node (or closes if it's @close).
				node.JumpTo = line.Substring(2).Trim();
				continue;
			}
			if (line.StartsWith(">"))
			{
				string body = line.Substring(1).Trim();
				string? lineBg = null;
				Match bgMatch = NarrationBgRegex.Match(body);
				if (bgMatch.Success)
				{
					lineBg = NormalizeBackground(bgMatch.Groups[1].Value.Trim());
					body = body.Substring(0, bgMatch.Index).Trim();
				}
				(node.Narration ??= new List<string>()).Add(body);
				(node.NarrationBackgrounds ??= new List<string?>()).Add(lineBg);
				continue;
			}
			(node.NpcTextLines ??= new List<string>()).Add(line);
		}
		ResolveAliases(tree, questAliases);
		Validate(tree, sourceName, errors);
		return tree.Nodes.Count > 0 ? tree : null;
	}

	private static void ParseHeaderLine(string line, DialogTree tree, Dictionary<string, string> aliases, string src, int lineNo, List<string> errors)
	{
		Match alias = QuestAliasRegex.Match(line);
		if (alias.Success)
		{
			aliases[alias.Groups[1].Value] = alias.Groups[2].Value;
			return;
		}
		int colon = line.IndexOf(':');
		if (colon <= 0)
		{
			errors.Add($"{src}:{lineNo}: " + Loc.P_BadHeaderLine(line));
			return;
		}
		string key = line.Substring(0, colon).Trim().ToLowerInvariant();
		string val = line.Substring(colon + 1).Trim();
		switch (key)
		{
		case "trader":
		{
			string? name = ExtractQuoted(ref val);
			tree.TraderId = val.Trim();
			if (!string.IsNullOrEmpty(name))
			{
				tree.TraderName = name!;
			}
			break;
		}
		case "start":
			tree.StartNode = val;
			break;
		case "first":
			tree.FirstVisitNode = val;
			break;
		case "when":
			ParseWhen(val, tree, src, lineNo, errors);
			break;
		case "random":
			ParseRandom(val, tree, src, lineNo, errors);
			break;
		case "trigger":
			ParseTrigger(val, tree, src, lineNo, errors);
			break;
		case "tab":
			ParseTabGate(val, tree, src, lineNo, errors);
			break;
		default:
			errors.Add($"{src}:{lineNo}: " + Loc.P_UnknownHeaderKey(key));
			break;
		}
	}

	private static void ParseTabGate(string val, DialogTree tree, string src, int lineNo, List<string> errors)
	{
		if (string.Equals(val.Trim(), "always", StringComparison.OrdinalIgnoreCase))
		{
			tree.TabAlways = true;
			return;
		}
		Match m = Regex.Match(val, @"^if\s+(\S+?)=(\S+)$");
		if (!m.Success)
		{
			errors.Add($"{src}:{lineNo}: " + Loc.P_BadTabFormat());
			return;
		}
		tree.TabQuestId = m.Groups[1].Value;
		tree.TabShowWhenStatus = SplitStatuses(m.Groups[2].Value);
	}

	private static void ParseWhen(string val, DialogTree tree, string src, int lineNo, List<string> errors)
	{
		int arrow = val.LastIndexOf("->", StringComparison.Ordinal);
		if (arrow < 0)
		{
			errors.Add($"{src}:{lineNo}: " + Loc.P_WhenMissingArrow());
			return;
		}
		NodeCondition cond = new NodeCondition { Node = val.Substring(arrow + 2).Trim() };
		foreach (string part in val.Substring(0, arrow).Split(','))
		{
			Match m = Regex.Match(part.Trim(), @"^(level|standing)\s*(>=|<=)\s*(-?[\d.]+)$");
			if (!m.Success)
			{
				errors.Add($"{src}:{lineNo}: " + Loc.P_BadCondition(part.Trim()));
				continue;
			}
			double n = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
			bool ge = m.Groups[2].Value == ">=";
			if (m.Groups[1].Value == "level")
			{
				if (ge) cond.MinLevel = (int)n;
				else cond.MaxLevel = (int)n;
			}
			else
			{
				if (ge) cond.MinStanding = n;
				else cond.MaxStanding = n;
			}
		}
		(tree.NodeConditions ??= new List<NodeCondition>()).Add(cond);
	}

	private static void ParseRandom(string val, DialogTree tree, string src, int lineNo, List<string> errors)
	{
		string[] tokens = val.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
		if (tokens.Length < 2 || !tokens[0].EndsWith("%")
			|| !float.TryParse(tokens[0].TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out float chance))
		{
			errors.Add($"{src}:{lineNo}: " + Loc.P_BadRandomFormat());
			return;
		}
		RandomAfterRaid rar = new RandomAfterRaid { Chance = chance };
		for (int i = 1; i < tokens.Length; i++)
		{
			rar.Nodes.Add(tokens[i]);
		}
		tree.RandomAfterRaid = rar;
	}

	private static void ParseTrigger(string val, DialogTree tree, string src, int lineNo, List<string> errors)
	{
		string? prompt = ExtractQuoted(ref val);
		float[]? vector = null;
		Match vec = VectorRegex.Match(val);
		if (vec.Success)
		{
			vector = new float[3]
			{
				ParseF(vec.Groups[1].Value),
				ParseF(vec.Groups[2].Value),
				ParseF(vec.Groups[3].Value)
			};
			val = val.Remove(vec.Index, vec.Length);
		}
		string[] tokens = val.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
		if (tokens.Length < 2)
		{
			errors.Add($"{src}:{lineNo}: " + Loc.P_BadTriggerFormat());
			return;
		}
		switch (tokens[0].ToLowerInvariant())
		{
		case "raid":
			ParseRaidTrigger(tokens, vector, prompt, tree, src, lineNo, errors);
			break;
		case "hideout":
			ParseHideoutTrigger(tokens, vector, prompt, tree, src, lineNo, errors);
			break;
		default:
			errors.Add($"{src}:{lineNo}: " + Loc.P_UnknownTriggerType(tokens[0]));
			break;
		}
	}

	private static void ParseRaidTrigger(string[] tokens, float[]? pos, string? prompt, DialogTree tree, string src, int lineNo, List<string> errors)
	{
		if (pos == null)
		{
			errors.Add($"{src}:{lineNo}: " + Loc.P_RaidTriggerMissingCoords());
			return;
		}
		FirstVisitTrigger t = new FirstVisitTrigger
		{
			Map = tokens[1],
			Position = pos
		};
		if (!string.IsNullOrEmpty(prompt))
		{
			t.PromptText = prompt!;
		}
		for (int i = 2; i < tokens.Length; i++)
		{
			switch (tokens[i].ToLowerInvariant())
			{
			case "door":
				if (i + 1 < tokens.Length)
				{
					string[] dims = tokens[++i].Split('x', 'X', '×');
					if (dims.Length >= 1) t.DoorWidth = ParseF(dims[0]);
					if (dims.Length >= 2) t.DoorHeight = ParseF(dims[1]);
					if (dims.Length >= 3) t.DoorRotationY = ParseF(dims[2]);
				}
				break;
			case "dist":
				if (i + 1 < tokens.Length) t.MaxDistance = ParseF(tokens[++i]);
				break;
			case "radius":
				if (i + 1 < tokens.Length) t.HitRadius = ParseF(tokens[++i]);
				break;
			case "once":
				t.Once = true;
				break;
			case "repeat":
				t.Once = false;
				break;
			default:
				errors.Add($"{src}:{lineNo}: " + Loc.P_RaidTriggerUnknownParam(tokens[i]));
				break;
			}
		}
		(tree.RaidTriggers ??= new List<FirstVisitTrigger>()).Add(t);
	}

	private static void ParseHideoutTrigger(string[] tokens, float[]? offset, string? prompt, DialogTree tree, string src, int lineNo, List<string> errors)
	{
		HideoutAreaTrigger t = new HideoutAreaTrigger { AreaType = tokens[1] };
		if (!string.IsNullOrEmpty(prompt))
		{
			t.PromptText = prompt!;
		}
		if (offset != null)
		{
			t.Offset = offset;
		}
		for (int i = 2; i < tokens.Length; i++)
		{
			switch (tokens[i].ToLowerInvariant())
			{
			case "level":
				if (i + 1 < tokens.Length) t.RequiredLevel = (int)ParseF(tokens[++i]);
				break;
			case "dist":
				if (i + 1 < tokens.Length) t.MaxDistance = ParseF(tokens[++i]);
				break;
			case "node":
				if (i + 1 < tokens.Length) t.Node = tokens[++i];
				break;
			case "offset":
				break;
			case "if":
				if (i + 1 < tokens.Length)
				{
					string cond = tokens[++i];
					int eq = cond.IndexOf('=');
					if (eq > 0)
					{
						t.QuestId = cond.Substring(0, eq);
						t.ShowWhenStatus = SplitStatuses(cond.Substring(eq + 1));
					}
					else
					{
						errors.Add($"{src}:{lineNo}: " + Loc.P_BadIfCondition());
					}
				}
				break;
			case "free":
			case "door":
				// Free-standing look-to-interact spot (no native menu to merge into).
				t.FreeStanding = true;
				break;
			case "hit":
				if (i + 1 < tokens.Length) t.HitRadius = ParseF(tokens[++i]);
				t.FreeStanding = true;
				break;
			default:
				errors.Add($"{src}:{lineNo}: " + Loc.P_HideoutTriggerUnknownParam(tokens[i]));
				break;
			}
		}
		(tree.HideoutTriggers ??= new List<HideoutAreaTrigger>()).Add(t);
	}

	private static DialogOption? ParseOption(string s, string src, int lineNo, List<string> errors)
	{
		string? directives = null;
		int pipe = s.IndexOf(" | ", StringComparison.Ordinal);
		if (pipe >= 0)
		{
			directives = s.Substring(pipe + 3).Trim();
			s = s.Substring(0, pipe).TrimEnd();
		}
		string? target = null;
		int arrow = s.LastIndexOf(" -> ", StringComparison.Ordinal);
		if (arrow >= 0)
		{
			target = s.Substring(arrow + 4).Trim();
			s = s.Substring(0, arrow).TrimEnd();
		}
		if (s.Length == 0)
		{
			errors.Add($"{src}:{lineNo}: " + Loc.P_OptionMissingText());
			return null;
		}
		DialogOption opt = new DialogOption { Text = s };
		if (!string.IsNullOrEmpty(target))
		{
			if (string.Equals(target, "@trade", StringComparison.OrdinalIgnoreCase))
			{
				opt.Action = "openTrade";
			}
			else if (string.Equals(target, "@tasks", StringComparison.OrdinalIgnoreCase))
			{
				opt.Action = "openTasks";
			}
			else if (string.Equals(target, "@services", StringComparison.OrdinalIgnoreCase))
			{
				opt.Action = "openServices";
			}
			else if (string.Equals(target, "@close", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(target, "@leave", StringComparison.OrdinalIgnoreCase))
			{
				opt.Action = "close";
			}
			else
			{
				opt.Next = target;
			}
		}
		if (string.IsNullOrEmpty(directives))
		{
			return opt;
		}
		string? autoStatus = null;
		bool always = false;
		List<string>? explicitShow = null;
		foreach (string rawDirective in directives!.Split(new[] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries))
		{
			string d = rawDirective.Trim();
			if (d.Length == 0)
			{
				continue;
			}
			if (string.Equals(d, "once", StringComparison.OrdinalIgnoreCase))
			{
				opt.Once = true;
				continue;
			}
			if (string.Equals(d, "always", StringComparison.OrdinalIgnoreCase))
			{
				always = true;
				continue;
			}
			int colon = d.IndexOf(':');
			if (colon <= 0)
			{
				errors.Add($"{src}:{lineNo}: " + Loc.P_UnknownOptionDirective(d));
				continue;
			}
			string verb = d.Substring(0, colon).Trim().ToLowerInvariant();
			string arg = d.Substring(colon + 1).Trim();
			switch (verb)
			{
			case "accept":
				if (opt.Action == null)
				{
					opt.Action = "acceptQuest";
					opt.QuestId = arg;
					autoStatus = "AvailableForStart";
				}
				else
				{
					opt.AcceptQuestId = arg;
				}
				break;
			case "handover":
				if (opt.Action != null)
				{
					errors.Add($"{src}:{lineNo}: " + Loc.P_OneMainAction("handover", opt.Action!));
					break;
				}
				opt.Action = "handoverItems";
				// `handover: <questId> [label...]` — an optional label (e.g. 建筑材料) overrides the native window's
				// item-name title. Labels can't contain a comma (the directive delimiter) but may contain spaces.
				{
					int sp = arg.IndexOf(' ');
					if (sp > 0)
					{
						opt.QuestId = arg.Substring(0, sp);
						opt.HandoverLabel = arg.Substring(sp + 1).Trim().Trim('"');
					}
					else opt.QuestId = arg;
				}
				autoStatus = "Started";
				break;
			case "complete":
				if (opt.Action != null)
				{
					errors.Add($"{src}:{lineNo}: " + Loc.P_OneMainAction("complete", opt.Action!));
					break;
				}
				opt.Action = "completeQuest";
				opt.QuestId = arg;
				autoStatus = "AvailableForFinish";
				break;
			case "if":
			{
				int eq = arg.IndexOf('=');
				if (eq <= 0)
				{
					errors.Add($"{src}:{lineNo}: " + Loc.P_BadIfDirective());
					break;
				}
				if (string.IsNullOrEmpty(opt.QuestId))
				{
					opt.QuestId = arg.Substring(0, eq).Trim();
				}
				explicitShow = SplitStatuses(arg.Substring(eq + 1));
				break;
			}
			case "ifnot":
			{
				int eq = arg.IndexOf('=');
				if (eq <= 0)
				{
					errors.Add($"{src}:{lineNo}: " + Loc.P_BadIfnotDirective());
					break;
				}
				if (string.IsNullOrEmpty(opt.QuestId))
				{
					opt.QuestId = arg.Substring(0, eq).Trim();
				}
				opt.HideWhenStatus = SplitStatuses(arg.Substring(eq + 1));
				break;
			}
			case "standing":
			{
				string? standingTarget = null;
				string standingValue = arg;
				int eq = arg.IndexOf('=');
				if (eq > 0)
				{
					standingTarget = arg.Substring(0, eq).Trim();
					standingValue = arg.Substring(eq + 1).Trim();
				}
				if (double.TryParse(standingValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double standingDelta))
				{
					opt.StandingDelta = standingDelta;
					opt.StandingTarget = string.IsNullOrEmpty(standingTarget) ? null : standingTarget;
				}
				else
				{
					errors.Add($"{src}:{lineNo}: " + Loc.P_BadStandingValue(arg));
				}
				break;
			}
			case "setstatus":
			{
				// Set a quest's status WITHOUT accepting/completing it (e.g. raid-visit makes a Ragman quest
				// submittable, then the player hands it in at Ragman). Bare `setstatus: quest` = AvailableForFinish.
				int eq = arg.IndexOf('=');
				if (eq <= 0)
				{
					opt.SetStatusQuestId = arg.Trim();
					opt.SetStatusValue = 3;
					break;
				}
				opt.SetStatusQuestId = arg.Substring(0, eq).Trim();
				int? st = ParseStatus(arg.Substring(eq + 1));
				if (st == null)
					errors.Add($"{src}:{lineNo}: " + Loc.P_BadSetStatus(arg.Substring(eq + 1).Trim()));
				else
					opt.SetStatusValue = st;
				break;
			}
			default:
				errors.Add($"{src}:{lineNo}: " + Loc.P_UnknownOptionVerb(verb));
				break;
			}
		}
		if (explicitShow != null)
		{
			opt.ShowWhenStatus = explicitShow;
		}
		else if (autoStatus != null && !always)
		{
			opt.ShowWhenStatus = new List<string> { autoStatus };
		}
		return opt;
	}

	private static void ResolveAliases(DialogTree tree, Dictionary<string, string> aliases)
	{
		if (aliases.Count == 0)
		{
			return;
		}
		string? Resolve(string? v)
		{
			return (v != null && aliases.TryGetValue(v, out string full)) ? full : v;
		}
		foreach (DialogNode node in tree.Nodes.Values)
		{
			foreach (DialogOption opt in node.Options)
			{
				opt.QuestId = Resolve(opt.QuestId);
				opt.AcceptQuestId = Resolve(opt.AcceptQuestId);
				opt.SetStatusQuestId = Resolve(opt.SetStatusQuestId);
			}
		}
		if (tree.HideoutTriggers != null)
		{
			foreach (HideoutAreaTrigger t in tree.HideoutTriggers)
			{
				t.QuestId = Resolve(t.QuestId);
			}
		}
		tree.TabQuestId = Resolve(tree.TabQuestId);
	}

	private static void Validate(DialogTree tree, string src, List<string> errors)
	{
		if (tree.Nodes.Count == 0)
		{
			errors.Add($"{src}: " + Loc.P_NoNodes());
			return;
		}
		if (string.IsNullOrEmpty(tree.StartNode) || !tree.Nodes.ContainsKey(tree.StartNode))
		{
			errors.Add($"{src}: " + Loc.P_StartNodeMissing(tree.StartNode));
		}
		if (!string.IsNullOrEmpty(tree.FirstVisitNode) && !tree.Nodes.ContainsKey(tree.FirstVisitNode!))
		{
			errors.Add($"{src}: " + Loc.P_FirstNodeMissing(tree.FirstVisitNode!));
		}
		foreach (KeyValuePair<string, DialogNode> kv in tree.Nodes)
		{
			string? jump = kv.Value.JumpTo;
			if (!string.IsNullOrEmpty(jump) && jump != "@start" && jump != "@close" && jump != "@leave" && !tree.Nodes.ContainsKey(jump!))
			{
				errors.Add($"{src}: " + Loc.P_NarrationJumpMissing(kv.Key, jump!));
			}
			foreach (DialogOption opt in kv.Value.Options)
			{
				if (!string.IsNullOrEmpty(opt.Next) && opt.Next != "@start" && !tree.Nodes.ContainsKey(opt.Next!))
				{
					errors.Add($"{src}: " + Loc.P_OptionTargetMissing(kv.Key, opt.Next!));
				}
			}
		}
		if (tree.NodeConditions != null)
		{
			foreach (NodeCondition cond in tree.NodeConditions)
			{
				if (!tree.Nodes.ContainsKey(cond.Node))
				{
					errors.Add($"{src}: " + Loc.P_WhenTargetMissing(cond.Node));
				}
			}
		}
		if (tree.RandomAfterRaid != null)
		{
			foreach (string n in tree.RandomAfterRaid.Nodes)
			{
				if (!tree.Nodes.ContainsKey(n))
				{
					errors.Add($"{src}: " + Loc.P_RandomTargetMissing(n));
				}
			}
		}
	}

	private static string? ExtractQuoted(ref string s)
	{
		Match m = QuotedRegex.Match(s);
		if (!m.Success)
		{
			return null;
		}
		s = s.Remove(m.Index, m.Length).Trim();
		return m.Groups[1].Value;
	}

	private static int? ParseStatus(string s)
	{
		s = s.Trim();
		if (int.TryParse(s, out int n) && n >= 0 && n <= 5) return n;
		switch (s.ToLowerInvariant())
		{
			case "locked": return 0;
			case "availableforstart": return 1;
			case "started": return 2;
			case "availableforfinish": return 3;
			case "success": return 4;
			case "fail": return 5;
			default: return null;
		}
	}

	private static List<string> SplitStatuses(string s)
	{
		List<string> list = new List<string>();
		foreach (string part in s.Split('/'))
		{
			string p = part.Trim();
			if (p.Length > 0)
			{
				list.Add(p);
			}
		}
		return list;
	}

	private static float ParseF(string s)
	{
		return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0f;
	}

	private static string NormalizeBackground(string value)
	{
		if (value.IndexOf('/') >= 0 || value.IndexOf('\\') >= 0)
		{
			return value;
		}
		return "backgrounds/" + value;
	}
}
