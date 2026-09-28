using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace VisitAPI.Native;

public static class DialogConfirm
{
    static readonly Dictionary<string, string> _byLine = new();

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

    public static string KeyFor(string lineId)
    {
        if (string.IsNullOrEmpty(lineId)) return null;
        lock (_byLine) return _byLine.TryGetValue(lineId, out var key) ? key : null;
    }
}
