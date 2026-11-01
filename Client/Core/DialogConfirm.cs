using System.Collections.Generic;
using EFT;
using Newtonsoft.Json.Linq;

namespace VisitAPI.Native;

public static class DialogConfirm
{
    static readonly Dictionary<string, string> _byLine = new();
    /// 10-15 SORA（站边要弹和上交箱子一样的窗）：.dlg 选项的 `confirm: 键名 [-> 节点]`。注册对话模板时按行 id 记下键名和「否」的去处；
    /// 和服务端那张表（原版数据的 ConfirmationKey）分开存，拉表 Clear 不会把它清掉
    static readonly Dictionary<string, (string key, MongoID? no)> _custom = new();

    public static void Fetch() => Plugin.Instance.StartCoroutine(VisitHttp.Fetch("/visitapi/dialogue/confirm", TryParse, "[choice]", _ => { }));

    static bool TryParse(string body)
    {
        try
        {
            if (JObject.Parse(body)["data"] is not JObject data) return false;
            lock (_byLine)
            {
                _byLine.Clear();
                foreach (var p in data.Properties())
                {
                    var key = p.Value?.Value<string>();
                    if (!string.IsNullOrEmpty(key)) _byLine[p.Name] = key;
                }
            }
            return true;
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("[choice] failed to parse the choice table: " + e.Message); return false; }
    }

    public static void Put(string lineId, string key, MongoID? noDialog)
    {
        if (string.IsNullOrEmpty(lineId) || string.IsNullOrEmpty(key)) return;
        lock (_custom) _custom[lineId] = (key, noDialog);
    }

    public static string KeyFor(string lineId)
    {
        if (string.IsNullOrEmpty(lineId)) return null;
        lock (_custom) if (_custom.TryGetValue(lineId, out var c)) return c.key;
        lock (_byLine) return _byLine.TryGetValue(lineId, out var key) ? key : null;
    }

    /// <summary>点「否」要跳去的对话；null = 照 1.1 退回选项页</summary>
    public static MongoID? NoTarget(string lineId)
    {
        if (string.IsNullOrEmpty(lineId)) return null;
        lock (_custom) return _custom.TryGetValue(lineId, out var c) ? c.no : null;
    }
}
