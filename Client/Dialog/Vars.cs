using EFT;

namespace VisitAPI.Native;

/// <summary>
/// `set: 名字=整数` 的变量体系。本地写入走引擎自己的 DialogSetVariableAction(Profile 域)；
/// 这里只负责名字→id 的映射和把同一笔同步到服务端 pmc.Variables（重登/换战局才不丢，旧 DEV_NOTES #66）。
/// </summary>
public static class Vars
{
    /// <summary>名字 → 变量 id。作者直接写 24 位十六进制就原样用，否则按名字算固定 id（同名永远同 id，跨商人通用）。</summary>
    public static MongoID Id(string name) =>
        name.Length == 24 && System.Text.RegularExpressions.Regex.IsMatch(name, "^[0-9a-fA-F]{24}$")
            ? new MongoID(name) : DialogTemplateBuilder.Id("visitapi.var", name);

    public static void Sync(MongoID id, int value)
    {
        Plugin.Log.LogDebug($"[var] {id} = {value}");
        VisitHttp.Post("/visitapi/variable/set", "{\"variableId\":\"" + id + "\",\"value\":" + value + "}", "[var]");
    }
}
