using EFT;

namespace VisitAPI.Native;

public static class Vars
{
    public static MongoID Id(string name) =>
        name.Length == 24 && System.Text.RegularExpressions.Regex.IsMatch(name, "^[0-9a-fA-F]{24}$")
            ? new MongoID(name) : DialogTemplateBuilder.Id("visitapi.var", name);

    public static void Sync(MongoID id, int value)
    {
        Plugin.Log.LogDebug($"[var] {id} = {value}");
        VisitHttp.Post("/visitapi/variable/set", "{\"variableId\":\"" + id + "\",\"value\":" + value + "}", "[var]");
    }
}
