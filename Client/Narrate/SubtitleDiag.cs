using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.AnimationSequencePlayer;
using EFT.Dialogs;
using EFT.GlobalEvents;
using EFT.UI;
using HarmonyLib;

namespace VisitAPI.Native;

public static class SubtitleDiag
{
    static string Describe(IEnumerable<SubtitleParams> cc)
    {
        if (cc == null) return "null";
        try
        {
            var list = cc.ToList();
            return list.Count + " 条 [" + string.Join(" ", list.Select(s => s == null ? "?" : $"{Key(s)}@{s.Start:0.##}-{s.End:0.##}{(HasText(s) ? "" : "(无文案)")}")) + "]";
        }
        catch { return "<读不出>"; }
    }
    static string Key(SubtitleParams s) => s?.Key == null ? "?" : s.Key.Length > 8 ? s.Key.Substring(0, 8) : s.Key;
    static bool HasText(SubtitleParams s)
    {
        try { var t = s?.Key?.Localized(); return !string.IsNullOrWhiteSpace(t) && t != s.Key; }
        catch { return false; }
    }

    [HarmonyPatch(typeof(TraderAnimationController), nameof(TraderAnimationController.ExecuteDialogOption))]
    public static class LineStart
    {
        static void Prefix(CombinedAnimationData animationData)
        {
            if (!Narrating.Now) return;
            Plugin.Log.LogDebug("[subtitle] 商人台词开播：字幕 " + Describe(animationData?.subtitleKeysWithParams) + $"，动画 {animationData?.animKeysWithParams?.Count ?? 0} 段，口型 {animationData?.lipSyncKeysWithParams?.Count ?? 0} 段");
        }
    }

    [HarmonyPatch(typeof(SubtitlesView), nameof(SubtitlesView.method_0))]
    public static class Shown
    {
        static void Postfix(SubtitlesView __instance, SubtitlesEvent subtitlesEvent)
        {
            if (!Narrating.Now) return;
            Plugin.Log.LogDebug($"[subtitle] 字幕视图收到 {subtitlesEvent?.SubtitlesSource}：{Describe(subtitlesEvent?.CcData)}（视图 {(__instance != null && __instance.gameObject.activeInHierarchy ? "在屏上" : "没激活")}）");
        }
    }

    [HarmonyPatch(typeof(SubtitlesView), nameof(SubtitlesView.method_1))]
    public static class Ended
    {
        static void Prefix(SubtitlesEndEvent endEvent)
        {
            if (!Narrating.Now) return;
            var trace = Environment.StackTrace.Split('\n').Skip(3).Take(6).Select(l => l.Trim()).ToArray();
            Plugin.Log.LogDebug($"[subtitle] 收到字幕结束事件（{endEvent?.SubtitlesSource}），当前字幕被清掉。来源：\n  " + string.Join("\n  ", trace));
        }
    }
}
