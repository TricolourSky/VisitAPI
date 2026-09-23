using System.IO;

namespace VisitAPI;

/// <summary>
/// 客户端读盘的两个内容目录（09-23 文件树整理）：
/// `BepInEx\plugins\VisitAPI\ui\` = 框架自己的两个界面包（原 bundles\），`BepInEx\plugins\VisitAPI\rooms\` = 商人房间包（原 scenes\bundles\vendors\）。
/// 老目录还在就照读并提示改名；两个都没有时返回新目录，调用方自己报「文件不在」。剧本区（config\VisitAPI）不在这里，形状没变。
/// </summary>
public static class VisitPaths
{
    static string _rooms, _ui;

    public static string PluginDir => Path.GetDirectoryName(typeof(Plugin).Assembly.Location);

    public static string Rooms => _rooms ??= Pick(
        Path.Combine(BepInEx.Paths.PluginPath, "VisitAPI", "rooms"),
        Path.Combine(BepInEx.Paths.PluginPath, "VisitAPI", "scenes", "bundles", "vendors"), "rooms");

    public static string Ui => _ui ??= Pick(Path.Combine(PluginDir, "ui"), Path.Combine(PluginDir, "bundles"), "ui");

    static string Pick(string now, string legacy, string what)
    {
        if (Directory.Exists(now)) return now;
        if (!Directory.Exists(legacy)) return now;
        Plugin.Log.LogWarning($"[paths] 还在用老目录 {legacy}，请把它改名为 {what}\\（下个版本不再认老名字）");
        return legacy;
    }
}
