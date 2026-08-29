using System;

namespace VisitAPI;

public static class Loc
{
    public static string Mode = "auto";
    public static Func<string> GameCulture;

    static bool Zh => Mode == "zh" || (Mode != "en" && (GameCulture?.Invoke() ?? "ch") == "ch");

    public static string Pick(string zh, string en) => Zh ? zh : en;
}
