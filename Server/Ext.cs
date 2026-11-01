using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace VisitAPI.Server;

/// <summary>从任务 JSON 里取值的小工具。
/// SPT 不认识的字段（visitapi、mailSettings、isStoryQuest……）在 ExtensionData 里，值是 JsonElement；
/// 取不到或类型不对一律当没写：对象 / 字符串给 null，标记给 false，列表给空表。</summary>
internal static class Ext
{
    /// 扩展字段 key 的值，是这种 JSON 类型才给
    public static JsonElement? Get(Dictionary<string, object> ext, string key, JsonValueKind kind) =>
        ext != null && ext.TryGetValue(key, out var v) && v is JsonElement e && e.ValueKind == kind ? e : null;

    /// 扩展字段 key 写的是 true
    public static bool IsTrue(Dictionary<string, object> ext, string key) => Get(ext, key, JsonValueKind.True) != null;

    /// 任务的 visitapi 对象
    public static JsonElement? Visit(Dictionary<string, object> ext) => Get(ext, "visitapi", JsonValueKind.Object);

    /// 1.1 的邀请信设置 mailSettings：是对象且 isEnabled = true 才给
    public static JsonElement? MailSettings(Dictionary<string, object> ext) =>
        Get(ext, "mailSettings", JsonValueKind.Object) is { } m && Flag(m, "isEnabled") ? m : null;

    public static bool Flag(JsonElement? vx, string name) => vx?.TryGetProperty(name, out var p) == true && p.ValueKind == JsonValueKind.True;

    public static string Str(JsonElement? vx, string name) => vx?.TryGetProperty(name, out var p) == true && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    public static List<string> StrList(JsonElement? vx, string name) =>
        vx?.TryGetProperty(name, out var p) == true && p.ValueKind == JsonValueKind.Array
            ? p.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()).ToList() : new List<string>();

    public static double? Num(JsonElement? vx, string name) => vx?.TryGetProperty(name, out var p) == true && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : null;

    public static JsonElement? Obj(JsonElement? vx, string name) => vx?.TryGetProperty(name, out var p) == true && p.ValueKind == JsonValueKind.Object ? p : null;

    /// Quest 类条件点名的任务 id（Target 写单个或列表都认，空的跳过）；别的条件类型给空
    public static IEnumerable<string> QuestCondTargets(QuestCondition c)
    {
        if (c.ConditionType != "Quest" || c.Target == null) yield break;
        foreach (var t in c.Target.IsItem ? new List<string> { c.Target.Item } : c.Target.List ?? new List<string>())
            if (!string.IsNullOrEmpty(t)) yield return t;
    }
}
