using System;
using EFT;
using EFT.Console.Core;
using EFT.UI;

namespace VisitAPI.Native;

/// <summary>游戏自带控制台（主菜单按 ` 键）里的几条救急命令，09-24 为 SORA 卡在陨落星辰起点写的：
/// 枪匠那段对话里选了「收到。谢谢你的信息」而不是追问飞机，1.1 数据里唯一给 6914f6cb 置位的台词就错过了，陨落星辰的桥接任务永远完不成。
/// visit_setvar 直接把档案变量写上（本地 + 服务端），任务引擎会像对话里赋值一样触发条件。</summary>
public class VisitConsole   // 不能是 static class：RegisterCommandGroup<T> 要拿它当泛型参数，它只扫 public static 方法
{
    static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        try
        {
            ConsoleScreen.Processor.RegisterCommandGroup<VisitConsole>();
            _registered = true;
        }
        catch (Exception e) { Plugin.Log.LogWarning("[console] failed to register console commands: " + e.Message); }
    }

    // 10-16：也认剧本里的记号名（set: / ifvar: 写的那种，如 escort、side）——走 Vars.Id 同一套散列；24 位十六进制照旧当 id
    static bool Resolve(string nameOrId, out MongoID id)
    {
        id = default;
        if (string.IsNullOrWhiteSpace(nameOrId) || nameOrId.Contains(" ")) { ConsoleScreen.LogError("要么 24 位十六进制的变量 id，要么剧本里的记号名（不含空格）"); return false; }
        id = Vars.Id(nameOrId.Trim());
        return true;
    }

    [ConsoleCommand("visit_setvar", "", null, "VisitAPI：把档案变量写成指定值（本地 + 服务端），例：visit_setvar 6914f6cbbbbecd2082ac4c18 1 或 visit_setvar escort 2")]
    public static void SetVar(string variableId, int value)
    {
        if (!Resolve(variableId, out var id)) return;
        var s = VisitBackend.Variables;
        if (s == null) { ConsoleScreen.LogError("还没登录，拿不到档案"); return; }
        var before = s.GetVariableValue(id);
        Vars.Set(s, id, value);
        ConsoleScreen.Log($"[VisitAPI] 变量 {variableId}（{id}）: {before} → {value}（已同步服务端）");
    }

    [ConsoleCommand("visit_getvar", "", null, "VisitAPI：读一个档案变量的当前值，例：visit_getvar escort")]
    public static void GetVar(string variableId)
    {
        if (!Resolve(variableId, out var id)) return;
        var s = VisitBackend.Variables;
        if (s == null) { ConsoleScreen.LogError("还没登录，拿不到档案"); return; }
        ConsoleScreen.Log($"[VisitAPI] 变量 {variableId}（{id}）= {s.GetVariableValue(id)}");
    }

    [ConsoleCommand("visit_rescan", "", null, "VisitAPI：把任务书重新过一遍自动链（自动接 / 自动完成）")]
    public static void Rescan()
    {
        try { ChapterChain.Rescan(); ConsoleScreen.Log("[VisitAPI] 自动链已重扫"); }
        catch (Exception e) { ConsoleScreen.LogError("[VisitAPI] 重扫失败: " + e.Message); }
    }
}
