using System;
using HarmonyLib;

namespace VisitAPI.Native;

/// <summary>2026-09-07：有些插件把「进没进战局」记在 <c>GameWorld.OnGameStarted</c> 上——WTT-MenuOverhaul 的
/// <c>GameStateUtility.isInGame</c> 就是（反编译 1.3.0 查实：OnGameStartedPatch 置 true，OnGameEndedPatch 挂在
/// <c>Player.OnGameSessionEnd</c> 上才置 false）。访问也会建 GameWorld、也会触发 OnGameStarted，于是它以为进了战局，把自己挂起；
/// 访问退出不走 OnGameSessionEnd，那个开关永远复不了位 → 主菜单回到原版布局（SORA 截图：人物换回原版持刀站姿、按钮丢图标、位置跑偏）。
/// 退出访问时按插件名把这类开关复位：反射调它**自己的公开复位函数**，插件不在就什么都不做，不碰它的内部件。
/// 复位必须在主菜单重新 Show 之前（它的布局补丁挂在 MenuScreen.Show 后缀上、以这个开关为准），所以放在 controller.Hide 的前缀里。</summary>
public static class RaidFlagReset
{
    public static void Run()
    {
        Reset("MoxoPixel.MenuOverhaul.Utils.GameStateUtility", "ResetGameState", "WTT-MenuOverhaul");
    }

    static void Reset(string typeName, string method, string label)
    {
        try
        {
            var t = AccessTools.TypeByName(typeName);
            if (t == null) return;   // 插件没装
            var m = AccessTools.Method(t, method);
            if (m == null) { Plugin.Log.LogWarning($"[narrate] {label} 已装但找不到 {typeName}.{method}（版本变了？），战局标记没复位"); return; }
            m.Invoke(null, null);
            Plugin.Log.LogInfo($"[narrate] {label} 的战局标记已复位（访问期它把自己挂起了）");
        }
        catch (Exception e) { Plugin.Log.LogWarning($"[narrate] {label} 战局标记复位失败: " + e.Message); }
    }
}
