using System;
using Comfort.Common;
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

    static ProfileVariablesStorage Storage()
    {
        try { return Singleton<ClientApplication<IEftSession>>.Instance?.GetClientBackEndSession()?.Profile?.ProfileVariables; }
        catch { return null; }
    }

    [ConsoleCommand("visit_setvar", "", null, "VisitAPI：把档案变量写成指定值（本地 + 服务端），例：visit_setvar 6914f6cbbbbecd2082ac4c18 1")]
    public static void SetVar(string variableId, int value)
    {
        if (string.IsNullOrEmpty(variableId) || variableId.Length != 24) { ConsoleScreen.LogError("变量 id 要 24 位十六进制"); return; }
        var s = Storage();
        if (s == null) { ConsoleScreen.LogError("还没登录，拿不到档案"); return; }
        var id = new MongoID(variableId);
        var before = s.GetVariableValue(id);
        s.SetVariableValue(id, value);
        Vars.Sync(id, value);
        ConsoleScreen.Log($"[VisitAPI] 变量 {variableId} : {before} → {value}（已同步服务端）");
    }

    [ConsoleCommand("visit_getvar", "", null, "VisitAPI：读一个档案变量的当前值")]
    public static void GetVar(string variableId)
    {
        if (string.IsNullOrEmpty(variableId) || variableId.Length != 24) { ConsoleScreen.LogError("变量 id 要 24 位十六进制"); return; }
        var s = Storage();
        if (s == null) { ConsoleScreen.LogError("还没登录，拿不到档案"); return; }
        ConsoleScreen.Log($"[VisitAPI] 变量 {variableId} = {s.GetVariableValue(new MongoID(variableId))}");
    }

    static readonly System.Collections.Generic.Dictionary<string, string> TraderIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["prapor"] = "54cb50c76803fa8b248b4571", ["therapist"] = "54cb57776803fa99248b456e", ["fence"] = "579dc571d53a0658a154fbec",
        ["skier"] = "58330581ace78e27b8b10cee", ["peacekeeper"] = "5935c25fb3acc3127c3d8cd9", ["mechanic"] = "5a7c2eca46aef81a7ca2145d",
        ["ragman"] = "5ac3b934156ae10c4430e83c", ["jaeger"] = "5c0647fdd443bc2504c2d371",
    };

    [ConsoleCommand("visit_testinvite", "", null, "VisitAPI：让某个商人在聊天里显示一封测试邀请（只改显示，不改存档），例：visit_testinvite mechanic；visit_testinvite off 关掉")]
    public static void TestInvite(string trader)
    {
        if (string.IsNullOrEmpty(trader) || string.Equals(trader, "off", StringComparison.OrdinalIgnoreCase))
        {
            InviteState.TestTrader = null;
            ConsoleScreen.Log("[VisitAPI] test invite off");
            return;
        }
        var id = TraderIds.TryGetValue(trader, out var known) ? known : trader;
        if (id.Length != 24) { ConsoleScreen.LogError("用商人名（prapor / mechanic / jaeger …）或 24 位商人 id"); return; }
        InviteState.TestTrader = id;
        ConsoleScreen.Log($"[VisitAPI] test invite from {InviteState.TraderName(id)} ({id}) shown in chat; visit_testinvite off to remove");
    }

    [ConsoleCommand("visit_uidump", "", null, "VisitAPI：导出一个界面物体的完整结构到 BepInEx\\VisitAPI-uidump，例：visit_uidump chat（当前聊天窗，只导显示中的）/ visit_uidump ChatScreen")]
    public static void UiDumpCmd(string name)
    {
        if (string.Equals(name, "chat", StringComparison.OrdinalIgnoreCase))
        {
            var screen = ChatInviteTabs.Screen;
            if (screen == null) { ConsoleScreen.LogError("聊天窗还没打开过"); return; }
            var chatFile = UiDump.Write(screen.transform, "chat", 40, activeOnly: true);
            ConsoleScreen.Log(chatFile != null ? "[VisitAPI] ui dump -> " + chatFile : "[VisitAPI] ui dump failed, see log");
            return;
        }
        var t = string.IsNullOrEmpty(name) ? null : UiDump.Find(name);
        if (t == null) { ConsoleScreen.LogError("找不到物体：" + name); return; }
        var file = UiDump.Write(t, name);
        ConsoleScreen.Log(file != null ? "[VisitAPI] ui dump -> " + file : "[VisitAPI] ui dump failed, see log");
    }

    [ConsoleCommand("visit_scenedump", "", null, "VisitAPI：导出当前访问房间的渲染状态（灯、反射探针、粒子、雾、相机效果）到 BepInEx\\VisitAPI-uidump，和 1.1 对比用")]
    public static void SceneDumpCmd()
    {
        var file = SceneDump.Write();
        ConsoleScreen.Log(file != null ? "[VisitAPI] scene dump -> " + file : "[VisitAPI] scene dump failed, see log");
    }

    [ConsoleCommand("visit_patchreport", "", null, "VisitAPI：列出所有模组的 Harmony 补丁（哪个模组挂在哪个方法上）到 BepInEx\\VisitAPI-uidump，查模组冲突用")]
    public static void PatchReportCmd()
    {
        var file = PatchReport.Write();
        ConsoleScreen.Log(file != null ? "[VisitAPI] patch report -> " + file : "[VisitAPI] patch report failed, see log");
    }

    [ConsoleCommand("visit_rescan", "", null, "VisitAPI：把任务书重新过一遍自动链（自动接 / 自动完成）")]
    public static void Rescan()
    {
        try { ChapterChain.Rescan(); ConsoleScreen.Log("[VisitAPI] 自动链已重扫"); }
        catch (Exception e) { ConsoleScreen.LogError("[VisitAPI] 重扫失败: " + e.Message); }
    }
}
