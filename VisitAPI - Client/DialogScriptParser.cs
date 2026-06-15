using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace VisitAPI;

/// <summary>
/// 剧本式 .dlg 对话脚本解析器，输出与 JSON 完全相同的 DialogTree 模型。
/// 语法参考 docs/DLG_FORMAT.md；解析问题带行号写入 errors，不抛异常。
///
/// 文件结构：
///   文件头（第一个节点之前）： trader/start/first/when/random/trigger/quest 别名
///   节点：  &lt;节点名&gt; bg: 背景文件
///           &gt; 旁白行
///           普通行 = NPC 台词（多行即多段）
///           - 选项文本 -> 目标 | 指令, 指令...
/// </summary>
internal static class DialogScriptParser
{
	// 节点名只允许 ASCII 标识符，台词里的 [动作] / <富文本> 不会被误判
	private static readonly Regex NodeHeaderRegex = new Regex(@"^<([A-Za-z0-9_.\-]+)>(?:\s+bg:\s*(.+))?$");

	private static readonly Regex QuestAliasRegex = new Regex(@"^quest\s+([A-Za-z0-9_\-]+)\s*=\s*(\S+)$");

	private static readonly Regex VectorRegex = new Regex(@"\(\s*(-?[\d.]+)\s*,\s*(-?[\d.]+)\s*,\s*(-?[\d.]+)\s*\)");

	private static readonly Regex QuotedRegex = new Regex("\"([^\"]*)\"");

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
			if (line.StartsWith(">"))
			{
				(node.Narration ??= new List<string>()).Add(line.Substring(1).Trim());
				continue;
			}
			(node.NpcTextLines ??= new List<string>()).Add(line);
		}
		ResolveAliases(tree, questAliases);
		Validate(tree, sourceName, errors);
		return tree.Nodes.Count > 0 ? tree : null;
	}

	// ── 文件头 ─────────────────────────────────────────────────────────────────

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
			errors.Add($"{src}:{lineNo}: 无法识别的文件头行: {line}");
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
			errors.Add($"{src}:{lineNo}: 未知的文件头键 '{key}'");
			break;
		}
	}

	// tab: always              —— "拜访"页签始终显示（无视解锁/任务门控）
	// tab: if 任务=状态[/状态]  —— 仅当任务处于指定状态时显示（取代默认的"商人解锁后才显示"）
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
			errors.Add($"{src}:{lineNo}: tab 格式应为 'tab: always' 或 'tab: if 任务=状态[/状态]'");
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
			errors.Add($"{src}:{lineNo}: when 缺少 '-> 节点'");
			return;
		}
		NodeCondition cond = new NodeCondition { Node = val.Substring(arrow + 2).Trim() };
		foreach (string part in val.Substring(0, arrow).Split(','))
		{
			Match m = Regex.Match(part.Trim(), @"^(level|standing)\s*(>=|<=)\s*(-?[\d.]+)$");
			if (!m.Success)
			{
				errors.Add($"{src}:{lineNo}: 无法识别的条件 '{part.Trim()}'（支持 level>=N / level<=N / standing>=N / standing<=N）");
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
			errors.Add($"{src}:{lineNo}: random 格式应为 'random: 10% 节点1 节点2 ...'");
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
			errors.Add($"{src}:{lineNo}: trigger 格式应为 'trigger: raid 地图 (x, y, z) ...' 或 'trigger: hideout 区域 ...'");
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
			errors.Add($"{src}:{lineNo}: 未知触发器类型 '{tokens[0]}'（支持 raid / hideout）");
			break;
		}
	}

	private static void ParseRaidTrigger(string[] tokens, float[]? pos, string? prompt, DialogTree tree, string src, int lineNo, List<string> errors)
	{
		if (pos == null)
		{
			errors.Add($"{src}:{lineNo}: raid 触发器缺少坐标 (x, y, z)");
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
			default:
				errors.Add($"{src}:{lineNo}: raid 触发器未知参数 '{tokens[i]}'");
				break;
			}
		}
		tree.FirstVisitTrigger = t;
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
				break; // 向量已在前面统一提取，此关键字可写可省
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
						errors.Add($"{src}:{lineNo}: if 条件格式应为 '任务=状态[/状态]'");
					}
				}
				break;
			default:
				errors.Add($"{src}:{lineNo}: hideout 触发器未知参数 '{tokens[i]}'");
				break;
			}
		}
		(tree.HideoutTriggers ??= new List<HideoutAreaTrigger>()).Add(t);
	}

	// ── 选项 ───────────────────────────────────────────────────────────────────

	private static DialogOption? ParseOption(string s, string src, int lineNo, List<string> errors)
	{
		string? directives = null;
		// 第一个 " | " 之后全部当指令区，避免多个 | 指令时把中间段误并进目标节点名
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
			errors.Add($"{src}:{lineNo}: 选项缺少文本");
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
			else
			{
				opt.Next = target; // 含 @start
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
				errors.Add($"{src}:{lineNo}: 无法识别的选项指令 '{d}'");
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
					opt.AcceptQuestId = arg; // 组合：执行主动作的同时接取另一个任务
				}
				break;
			case "handover":
				if (opt.Action != null)
				{
					errors.Add($"{src}:{lineNo}: 一个选项只能有一个主动作（handover 之前已有 {opt.Action}）");
					break;
				}
				opt.Action = "handoverItems";
				opt.QuestId = arg;
				autoStatus = "Started";
				break;
			case "complete":
				if (opt.Action != null)
				{
					errors.Add($"{src}:{lineNo}: 一个选项只能有一个主动作（complete 之前已有 {opt.Action}）");
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
					errors.Add($"{src}:{lineNo}: if 指令格式应为 'if: 任务=状态[/状态]'");
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
					errors.Add($"{src}:{lineNo}: ifnot 指令格式应为 'ifnot: 任务=状态[/状态]'");
					break;
				}
				if (string.IsNullOrEmpty(opt.QuestId))
				{
					opt.QuestId = arg.Substring(0, eq).Trim();
				}
				opt.HideWhenStatus = SplitStatuses(arg.Substring(eq + 1));
				break;
			}
			default:
				errors.Add($"{src}:{lineNo}: 未知选项指令 '{verb}'");
				break;
			}
		}
		// accept/handover/complete 自动按任务状态门控；always 取消；if 显式覆盖
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

	// ── 收尾 ───────────────────────────────────────────────────────────────────

	// 任务别名统一在解析结束后回填，允许 'quest x = ...' 写在文件头任意位置
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
			errors.Add($"{src}: 没有任何节点");
			return;
		}
		if (string.IsNullOrEmpty(tree.StartNode) || !tree.Nodes.ContainsKey(tree.StartNode))
		{
			errors.Add($"{src}: start 节点 '{tree.StartNode}' 不存在");
		}
		if (!string.IsNullOrEmpty(tree.FirstVisitNode) && !tree.Nodes.ContainsKey(tree.FirstVisitNode!))
		{
			errors.Add($"{src}: first 节点 '{tree.FirstVisitNode}' 不存在");
		}
		foreach (KeyValuePair<string, DialogNode> kv in tree.Nodes)
		{
			foreach (DialogOption opt in kv.Value.Options)
			{
				if (!string.IsNullOrEmpty(opt.Next) && opt.Next != "@start" && !tree.Nodes.ContainsKey(opt.Next!))
				{
					errors.Add($"{src}: 节点 '{kv.Key}' 的选项指向不存在的节点 '{opt.Next}'");
				}
			}
		}
		if (tree.NodeConditions != null)
		{
			foreach (NodeCondition cond in tree.NodeConditions)
			{
				if (!tree.Nodes.ContainsKey(cond.Node))
				{
					errors.Add($"{src}: when 指向不存在的节点 '{cond.Node}'");
				}
			}
		}
		if (tree.RandomAfterRaid != null)
		{
			foreach (string n in tree.RandomAfterRaid.Nodes)
			{
				if (!tree.Nodes.ContainsKey(n))
				{
					errors.Add($"{src}: random 指向不存在的节点 '{n}'");
				}
			}
		}
	}

	// ── 工具 ───────────────────────────────────────────────────────────────────

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
		// 纯文件名默认放在 backgrounds/ 下；带路径的原样使用
		if (value.IndexOf('/') >= 0 || value.IndexOf('\\') >= 0)
		{
			return value;
		}
		return "backgrounds/" + value;
	}
}
