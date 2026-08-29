using System.Collections.Generic;
using EFT;
using EFT.Dialogs;

namespace VisitAPI.Native;

/// <summary>
/// `set: 名字=整数` 的落盘。引擎自己的 DialogSetVariableAction(Profile 域) 已把值写进本地 Profile.ProfileVariables，
/// 这里只负责把同一笔同步到服务端 pmc.Variables，重登/换战局才不丢。见 DEV_NOTES #66。
/// </summary>
public static class VariableService
{
    static readonly Dictionary<MongoID, (MongoID id, int value)> _byLine = new();

    /// <summary>名字 → 变量 id。作者直接写 24 位十六进制就原样用，否则按名字算一个固定 id（同名永远同 id，跨商人通用）。</summary>
    public static MongoID Id(string name) =>
        name.Length == 24 && System.Text.RegularExpressions.Regex.IsMatch(name, "^[0-9a-fA-F]{24}$") ? new MongoID(name) : DialogTemplateBuilder.Id("visitapi.var", name);

    public static void Register(MongoID lineId, string name, int value) => _byLine[lineId] = (Id(name), value);

    public static void Watch(BaseTraderDialogController dc)
    {
        dc.OnDialogChanged += dialog =>
        {
            if (dialog == null) return;
            dialog.OnExecuteLine += line =>
            {
                if (line?.Template != null && _byLine.TryGetValue(line.Template.Id, out var v)) Sync(v.id, v.value);
            };
        };
    }

    static void Sync(MongoID id, int value)
    {
        Plugin.Log.LogDebug($"[var] {id} = {value}");
        VisitHttp.Post("/visitapi/variable/set", "{\"variableId\":\"" + id + "\",\"value\":" + value + "}", "[var]");
    }
}
