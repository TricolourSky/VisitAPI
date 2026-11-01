using System;
using System.Collections.Generic;
using EFT;
using VisitAPI.Dialog;

namespace VisitAPI;

public static class Loc
{
    public static Func<string> GameCulture;

    static bool Zh => Code == "ch";

    public static string Pick(string zh, string en) => Zh ? zh : en;

    /// <summary>玩家要的语言代码（SPT 的 ch / en / ru…）：跟游戏语言（09-25 起插件没有语言设置），取不到按 ch。
    /// .dlg 的译文行按它取（DialogLangs.Pick / LocTable）。</summary>
    public static string Code => GameCulture?.Invoke() ?? "ch";

    /// <summary>.dlg 译文行按「游戏语言 → 英文 → 原文」取（10-26，SP-Mods 留言：俄语界面下 SORA 的对话全是中文）。
    /// 中文玩家没有 ch 译文就看原文；别的语言没有自己的译文先退英文，和界面文字（Pick）一个退法。LocTable 的字幕表也按这个顺序叠</summary>
    public static string Tr(Dictionary<string, string> tr, string original) =>
        DialogLangs.Pick(tr, Code, Zh ? original : DialogLangs.Pick(tr, "en", original));

    /// <summary>文案表里有这条键就用它，没有退回中英文兜底。</summary>
    public static string Text(string key, string zh, string en) => Key(key) ?? Pick(zh, en);

    /// <summary>文案表里这条键的字；没有（或表里就是键本身）返回 null。</summary>
    public static string Key(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        try
        {
            var t = key.Localized();
            return string.IsNullOrWhiteSpace(t) || t == key ? null : t;
        }
        catch { return null; }
    }

    /// <summary>商人昵称（`商人id Nickname`），文案表里没有就显示 id 本身。</summary>
    public static string TraderName(string traderId) => Key(traderId + " Nickname") ?? traderId;
}
