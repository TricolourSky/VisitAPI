using EFT;

namespace VisitAPI.Native;

public static class OnceService
{
    static MongoID Id(string kind, string trader, string key) => DialogTemplateBuilder.Id("visitapi.once", kind + "|" + trader + "|" + key);
    public static MongoID OnceId(string trader, string node, int option) => Id("once", trader, node + "|" + option);
    public static MongoID FirstId(string trader) => Id("first", trader, "");
    public static MongoID TriggerId(string trader, string key) => Id("trig", trader, key);

    public static bool Used(Profile profile, MongoID id) => profile?.ProfileVariables is { } s && s.GetVariableValue(id) != 0;

    public static void Mark(Profile profile, MongoID id)
    {
        if (profile?.ProfileVariables is { } s && s.GetVariableValue(id) != 1) Vars.Set(s, id, 1);
    }

    public static void Mark(Profile profile, (string trader, string node, int option) key) =>
        Mark(profile, OnceId(key.trader, key.node, key.option));
}
