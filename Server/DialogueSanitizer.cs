using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace VisitAPI.Server;

public static class DialogueSanitizer
{
    static readonly HashSet<string> Conditions = ["VariableValue", "QuestStatus", "TraderReputation", "QuestConditionStatus", "HasNewQuests", "MainLogicalGroup", "LogicalSubGroup", "HasItemForHandover", "ServiceAvailable", "CurrentTrader"];

    static readonly HashSet<string> Actions =
    [
        "SetVariable", "DiaryNote", "SwitchDialog", "QuitAction", "TradingScreenAction", "QuestsScreenAction", "SwitchQuestDialog", "SelectQuest", "AcceptQuest", "HandoverItem",
        "FinishQuest", "PlayerReward", "SelectSubService", "PurchaseService"
    ];

    const string ConstantVariable = "000000000000000000000000";

    static void Convert(JsonNode node)
    {
        if (node is JsonArray array)
        {
            foreach (var item in array) Convert(item);
            return;
        }
        if (node is not JsonObject obj) return;
        Convert(obj["Conditions"]);
        if (obj["type"] is not JsonValue typeValue || !typeValue.TryGetValue<string>(out var type)) return;
        bool? truth = type switch
        {
            "CompletableItem" => !(obj["isCompleted"] is JsonValue c && c.TryGetValue<bool>(out var done) && done),
            "HasFreeSpecialSlot" => obj["hasFreeSlot"] is JsonValue f && f.TryGetValue<bool>(out var free) && free,
            _ => null
        };
        if (truth == null) return;
        foreach (var key in obj.Select(kv => kv.Key).Where(k => k != "id" && k != "Id").ToList()) obj.Remove(key);
        obj["type"] = "VariableValue";
        obj["variableId"] = ConstantVariable;
        obj["value"] = truth.Value ? 0 : 1;
        obj["operator"] = "==";
    }

    public static void Clean(JsonObject element)
    {
        if (element["Lines"] is not JsonArray lines) return;
        foreach (var line in lines.OfType<JsonObject>()) Convert(line["Trigger"]);
        var removed = lines.OfType<JsonObject>().Where(line => Unsupported(line["Trigger"], Conditions) || Unsupported(line["Actions"], Actions)).ToList();
        foreach (var line in removed) lines.Remove(line);
    }

    /// <summary>1.3.4 B6：1.1 的随机台词按闭区间写——同一个随机变量的各变体 [Start, End] 拼起来正好覆盖 0..MaxValue-1；
    /// 0.16.9 的 RandomLineCondition.Test 按半开区间 [Start, End) 判，随机数取 Random.Range(0, MaxValue+1)（会掷到 MaxValue）。
    /// 这里把包里的数据换成引擎的判法：每个变体 End + 1；原来收尾在 MaxValue-1 的那个变体改成 End = MaxValue+1，把掷到 MaxValue 的那一格也吃进来。
    /// 这样访问内外（主菜单里经 @visit 进的零售对话也一样）都按原生判法出台词，客户端不用再补丁。
    /// 只动「看得出是闭区间」的组：同一对话元素里同一变量的变体最大 End 恰好是 MaxValue-1；已经有变体收尾在 MaxValue 的（本来就是半开区间写的）原样不动。</summary>
    public static void HalfOpenRandoms(JsonObject element)
    {
        if (element["Lines"] is not JsonArray lines) return;
        var groups = new Dictionary<string, List<JsonObject>>();
        foreach (var line in lines.OfType<JsonObject>())
        {
            if (line["Trigger"] is not JsonObject trigger || trigger["Random"] is not JsonObject random) continue;
            if (!TryInt(random["StartValue"], out _) || !TryInt(random["EndValue"], out _) || !TryInt(random["MaxValue"], out var max) || max <= 0) continue;
            var key = (random["VariableName"] is JsonValue v && v.TryGetValue<string>(out var name) ? name : "") + "|" + max;
            if (!groups.TryGetValue(key, out var list)) list = groups[key] = new List<JsonObject>();
            list.Add(random);
        }
        foreach (var group in groups.Values)
        {
            TryInt(group[0]["MaxValue"], out var max);
            var top = group.Max(r => { TryInt(r["EndValue"], out var e); return e; });
            if (top != max - 1) continue;
            foreach (var random in group)
            {
                TryInt(random["EndValue"], out var end);
                random["EndValue"] = end == max - 1 ? max + 1 : end + 1;
            }
        }
    }

    /// <summary>09-25 SORA 实机 Jaeger 开场只剩红色「返回」（errors.log：Dialog 68cc0914 has no lines. Generating fallback line）：
    /// 1.1 的随机台词带 GroupId，同一个随机变量可以被好几组开场白共用、各组上限还不一样（Jaeger 的 68cc0914…8f77：第 1 组上限 400，第 8 / 9 / 12 组上限 500）。
    /// 0.16.9 没有 GroupId，BaseTraderDialogController.GetRandomValue 按变量名缓存、只掷一次，范围取第一个来测它的条件的上限；
    /// DialogMainConditionGroup.Test 又是先测随机再测别的条件，于是排在前面的第 9 组先按 500 掷，新档（声望 &lt; 0.35）该走的第 1 组只覆盖 0..400，
    /// 掷到 400 以上整组落空。这里按 1.1 的分组把共用的变量拆开：一个随机变量在同一对话元素里出现了两个以上 GroupId，就给每组换一个由「变量 + 组号」派生的新变量名，
    /// 各组各掷各的，引擎走原生逻辑。随机变量名只是掷骰缓存的键，不是对话变量，不影响别处。</summary>
    public static void SplitRandomGroups(JsonObject element)
    {
        if (element["Lines"] is not JsonArray lines) return;
        var byVar = new Dictionary<string, List<JsonObject>>();
        foreach (var line in lines.OfType<JsonObject>())
        {
            if (line["Trigger"] is not JsonObject trigger || trigger["Random"] is not JsonObject random) continue;
            if (random["VariableName"] is not JsonValue v || !v.TryGetValue<string>(out var name) || string.IsNullOrEmpty(name)) continue;
            if (!byVar.TryGetValue(name, out var list)) list = byVar[name] = new List<JsonObject>();
            list.Add(random);
        }
        foreach (var (name, randoms) in byVar)
        {
            if (randoms.Select(GroupOf).Distinct().Count() < 2) continue;
            foreach (var random in randoms) random["VariableName"] = Derive(name, GroupOf(random));
        }
    }

    static string GroupOf(JsonObject random) => TryInt(random["GroupId"], out var g) ? g.ToString() : "none";

    /// 派生的变量名：24 位十六进制（MongoId 格式），同样的「变量 + 组号」每次都得到同一个
    static string Derive(string variable, string group) => Ids.Hex24(variable + "#random-group#" + group);

    static bool TryInt(JsonNode node, out long value)
    {
        value = 0;
        if (node is not JsonValue v) return false;
        if (v.TryGetValue<long>(out value)) return true;
        if (v.TryGetValue<int>(out var i))
        {
            value = i;
            return true;
        }
        if (v.TryGetValue<double>(out var d) && d == System.Math.Floor(d))
        {
            value = (long)d;
            return true;
        }
        return false;
    }

    public static int FixDanglingSwitches(JsonObject element, HashSet<string> knownIds)
    {
        if (element["Lines"] is not JsonArray lines) return 0;
        var count = 0;
        foreach (var line in lines.OfType<JsonObject>())
        {
            if (line["Actions"] is not JsonArray actions) continue;
            foreach (var action in actions.OfType<JsonObject>())
            {
                if (action["type"] is JsonValue typeValue && typeValue.TryGetValue<string>(out var type) && type == "SwitchDialog"
                    && action["dialogId"] is JsonValue idValue && idValue.TryGetValue<string>(out var target) && !knownIds.Contains(target))
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

    static bool Unsupported(JsonNode node, HashSet<string> allowed)
    {
        if (node is JsonArray array) return array.Any(item => Unsupported(item, allowed));
        if (node is not JsonObject obj) return false;
        if (obj["type"] is JsonValue typeNode && typeNode.TryGetValue<string>(out var type) && !allowed.Contains(type)) return true;
        return Unsupported(obj["Conditions"], allowed);
    }
}
