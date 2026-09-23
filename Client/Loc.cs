using System;

namespace VisitAPI;

public static class Loc
{
    public static string Mode = "auto";
    public static Func<string> GameCulture;

    static bool Zh => Mode == "zh" || (Mode != "en" && (GameCulture?.Invoke() ?? "ch") == "ch");

    public static string Pick(string zh, string en) => Zh ? zh : en;

    /// <summary>玩家要的语言代码（SPT 的 ch / en / ru…）：配置 Language 强制 zh / en 就是 ch / en，auto 跟游戏语言。
    /// .dlg 的译文行按它取（DialogLangs.Pick / LocTable），09-23 SORA 定「跟插件配置，auto 看游戏语言」。</summary>
    public static string Code => Mode == "zh" ? "ch" : Mode == "en" ? "en" : (GameCulture?.Invoke() ?? "ch");
}
