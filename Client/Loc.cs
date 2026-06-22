using System;
using System.Reflection;
using HarmonyLib;

namespace VisitAPI
{
    // Bilingual (中/EN) text for VisitAPI's OWN user-facing strings — player-facing UI and author-facing .dlg
    // parse diagnostics. Demo content (a trader's .dlg/quest text) is localized by the author via db/locales; this
    // covers only the strings the FRAMEWORK itself generates. Language follows EFT's current UI language by default
    // (auto), overridable via the `General/Language` config (auto/zh/en).
    internal static class Loc
    {
        internal enum Mode { Auto, Zh, En }

        private static Mode _mode = Mode.Auto;
        internal static void SetMode(Mode m) => _mode = m;

        internal static bool IsZh
        {
            get
            {
                switch (_mode)
                {
                    case Mode.Zh: return true;
                    case Mode.En: return false;
                    default: return IsChinese(ReadEftLanguage());
                }
            }
        }

        internal static string Pick(string zh, string en) => IsZh ? zh : en;

        private static bool IsChinese(string code) =>
            !string.IsNullOrEmpty(code) &&
            (code.StartsWith("ch", StringComparison.OrdinalIgnoreCase)   // EFT uses "ch" for Chinese
             || code.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
             || code.Equals("cn", StringComparison.OrdinalIgnoreCase));

        // EFT's current language code, read from the same source as the game's own Localized() —
        // LocaleManagerClass.LocaleManagerClass (static singleton) -> String_0 ("ch"/"en"/"ru"/...). Fail-soft to
        // "en". Read lazily (not at plugin Awake) so the language is already set by the time UI/parse text renders.
        private static bool _bound;
        private static PropertyInfo? _instanceProp;
        private static PropertyInfo? _langProp;

        private static string ReadEftLanguage()
        {
            try
            {
                if (!_bound)
                {
                    _bound = true;
                    Type? t = AccessTools.TypeByName("LocaleManagerClass");
                    _instanceProp = t?.GetProperty("LocaleManagerClass", BindingFlags.Public | BindingFlags.Static);
                    _langProp = t?.GetProperty("String_0", BindingFlags.Public | BindingFlags.Instance);
                }
                object? inst = _instanceProp?.GetValue(null);
                return (inst != null ? _langProp?.GetValue(inst) as string : null) ?? "en";
            }
            catch { return "en"; }
        }

        // ===== Player-facing UI =====
        internal static string RowEnd => Pick("（结束）", "(End)");
        internal static string RowContinue => Pick("继续…", "Continue…");
        internal static string DefaultTalkLabel => Pick("对话", "Talk");
        internal static string DefaultVisitPrompt => Pick("拜访", "Visit");

        // ===== .dlg parse diagnostics (author-facing, BepInEx log). The "<src>:<line>: " prefix is added at the
        // call site; these are just the message bodies, both languages interpolated with the same args. =====
        internal static string P_BadHeaderLine(string line) => Pick($"无法识别的文件头行: {line}", $"Unrecognized header line: {line}");
        internal static string P_UnknownHeaderKey(string key) => Pick($"未知的文件头键 '{key}'", $"Unknown header key '{key}'");
        internal static string P_BadTabFormat() => Pick("tab 格式应为 'tab: always' 或 'tab: if 任务=状态[/状态]'", "tab format must be 'tab: always' or 'tab: if quest=status[/status]'");
        internal static string P_WhenMissingArrow() => Pick("when 缺少 '-> 节点'", "when is missing '-> node'");
        internal static string P_BadCondition(string part) => Pick($"无法识别的条件 '{part}'（支持 level>=N / level<=N / standing>=N / standing<=N）", $"Unrecognized condition '{part}' (supported: level>=N / level<=N / standing>=N / standing<=N)");
        internal static string P_BadRandomFormat() => Pick("random 格式应为 'random: 10% 节点1 节点2 ...'", "random format must be 'random: 10% node1 node2 ...'");
        internal static string P_BadTriggerFormat() => Pick("trigger 格式应为 'trigger: raid 地图 (x, y, z) ...' 或 'trigger: hideout 区域 ...'", "trigger format must be 'trigger: raid <map> (x, y, z) ...' or 'trigger: hideout <area> ...'");
        internal static string P_UnknownTriggerType(string type) => Pick($"未知触发器类型 '{type}'（支持 raid / hideout）", $"Unknown trigger type '{type}' (supported: raid / hideout)");
        internal static string P_RaidTriggerMissingCoords() => Pick("raid 触发器缺少坐标 (x, y, z)", "raid trigger is missing coordinates (x, y, z)");
        internal static string P_RaidTriggerUnknownParam(string tok) => Pick($"raid 触发器未知参数 '{tok}'", $"Unknown raid trigger parameter '{tok}'");
        internal static string P_BadIfCondition() => Pick("if 条件格式应为 '任务=状态[/状态]'", "if condition format must be 'quest=status[/status]'");
        internal static string P_HideoutTriggerUnknownParam(string tok) => Pick($"hideout 触发器未知参数 '{tok}'", $"Unknown hideout trigger parameter '{tok}'");
        internal static string P_OptionMissingText() => Pick("选项缺少文本", "Option is missing its text");
        internal static string P_UnknownOptionDirective(string d) => Pick($"无法识别的选项指令 '{d}'", $"Unrecognized option directive '{d}'");
        internal static string P_OneMainAction(string verb, string existing) => Pick($"一个选项只能有一个主动作（{verb} 之前已有 {existing}）", $"An option can only have one main action ({verb}, but it already has {existing})");
        internal static string P_BadIfDirective() => Pick("if 指令格式应为 'if: 任务=状态[/状态]'", "if directive format must be 'if: quest=status[/status]'");
        internal static string P_BadIfnotDirective() => Pick("ifnot 指令格式应为 'ifnot: 任务=状态[/状态]'", "ifnot directive format must be 'ifnot: quest=status[/status]'");
        internal static string P_BadStandingValue(string arg) => Pick($"standing 数值无法解析 '{arg}'（应为 +0.02 / -0.05 / 商人ID=+0.01）", $"Cannot parse standing value '{arg}' (expected +0.02 / -0.05 / traderId=+0.01)");
        internal static string P_BadSetStatus(string s) => Pick($"setstatus 状态无法识别 '{s}'（应为 Locked/AvailableForStart/Started/AvailableForFinish/Success/Fail 或 0-5）", $"Unrecognized setstatus value '{s}' (expected Locked/AvailableForStart/Started/AvailableForFinish/Success/Fail or 0-5)");
        internal static string P_UnknownOptionVerb(string verb) => Pick($"未知选项指令 '{verb}'", $"Unknown option directive '{verb}'");
        internal static string P_NoNodes() => Pick("没有任何节点", "No nodes defined");
        internal static string P_StartNodeMissing(string n) => Pick($"start 节点 '{n}' 不存在", $"start node '{n}' does not exist");
        internal static string P_FirstNodeMissing(string n) => Pick($"first 节点 '{n}' 不存在", $"first node '{n}' does not exist");
        internal static string P_NarrationJumpMissing(string node, string jump) => Pick($"节点 '{node}' 的旁白跳转 '-> {jump}' 指向不存在的节点", $"node '{node}' narration jump '-> {jump}' points to a non-existent node");
        internal static string P_OptionTargetMissing(string node, string next) => Pick($"节点 '{node}' 的选项指向不存在的节点 '{next}'", $"node '{node}' has an option pointing to non-existent node '{next}'");
        internal static string P_WhenTargetMissing(string node) => Pick($"when 指向不存在的节点 '{node}'", $"when points to non-existent node '{node}'");
        internal static string P_RandomTargetMissing(string n) => Pick($"random 指向不存在的节点 '{n}'", $"random points to non-existent node '{n}'");
    }
}
