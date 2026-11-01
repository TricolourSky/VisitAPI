using EFT;
using VisitAPI.Dialog;

namespace VisitAPI.Native;

public static class Vars
{
    /// 剧本里的记号名（set: / ifvar: 写的那种）→ 档案变量 id；本来就是 24 位十六进制的照用
    public static MongoID Id(string name) =>
        DialogParser.IsQuestId(name) ? new MongoID(name) : DialogTemplateBuilder.Id("visitapi.var", name);

    public static void Sync(MongoID id, int value) =>
        VisitHttp.Post("/visitapi/variable/set", "{\"variableId\":\"" + id + "\",\"value\":" + value + "}", "[var]");

    /// 本地档案先改（任务引擎像对话里赋值一样当场触发条件），再同步服务端
    public static void Set(ProfileVariablesStorage storage, MongoID id, int value)
    {
        storage.SetVariableValue(id, value);
        Sync(id, value);
    }
}
