using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.AnimationSequencePlayer;
using EFT.GlobalEvents;
using EFT.UI;
using HarmonyLib;
using UnityEngine;

namespace VisitAPI.Native;

/// <summary>09-25 SORA 实机「有些商人的对话没有字幕」：原生 SubtitlesView 到 Show() 时才订阅字幕事件，而访问里商人第一句台词的字幕事件
/// 在对话屏打开之前就发出来了（29 次访问全部如此），那一句的字幕就丢了。这里访问期间自己也订阅，记下最近一次字幕（事件对象是池化复用的，拷一份）；
/// 对话屏的 SubtitlesView.Show 之后如果这句还没播完，就把时间轴往前挪掉已经过去的时间（和原生 Subtitles 一样按 AudioSettings.dspTime 算）交给原生接着放，
/// 和语音对得上。另外中途跳过台词时，上一句的结束事件会晚于下一句的开始事件到达、把新字幕清掉（日志里 6 句）：新字幕开始后 0.3 秒内的结束事件不理。</summary>
public static class SubtitleReplay
{
    const double StaleEndWindow = 0.3;

    static bool _armed;
    static ESubtitlesSource _source;
    static List<SubtitleParams> _lines;
    static double _at = -1;

    public static void Arm()
    {
        if (_armed) return;
        try
        {
            var ge = GlobalEventsController.Instance;
            if (ge == null) return;
            ge.SubscribeOnEvent<SubtitlesEvent>(OnSubtitles);
            ge.SubscribeOnEvent<SubtitlesEndEvent>(OnEnd);
            _armed = true;
        }
        catch (Exception e) { Plugin.Log.LogWarning("[subtitle] could not subscribe to subtitle events; first-line subtitles may be missing: " + e.Message); }
    }

    static void OnSubtitles(SubtitlesEvent e)
    {
        if (!Narrating.Now || e?.CcData == null) return;
        _source = e.SubtitlesSource;
        _lines = e.CcData.Where(p => p != null).Select(p => new SubtitleParams { Key = p.Key, Start = p.Start, End = p.End }).ToList();
        _at = AudioSettings.dspTime;
    }

    static void OnEnd(SubtitlesEndEvent e)
    {
        if (e == null || e.SubtitlesSource != _source || _at < 0) return;
        if (AudioSettings.dspTime - _at < StaleEndWindow) return;   // 上一句晚到的结束事件，不算这句结束
        _lines = null; _at = -1;
    }

    internal static bool IsStaleEnd(ESubtitlesSource source) =>
        Narrating.Now && _at >= 0 && source == _source && AudioSettings.dspTime - _at < StaleEndWindow;

    /// 这句还剩多少没放：时间轴整体往前挪已经放过的秒数，已经放完的行丢掉；一行都不剩就返回 null
    internal static List<SubtitleParams> Remaining(ESubtitlesSource source)
    {
        if (!Narrating.Now || _lines == null || _at < 0 || source != _source) return null;
        var e = (float)(AudioSettings.dspTime - _at);
        var left = _lines.Where(l => l.End > e).Select(l => new SubtitleParams { Key = l.Key, Start = l.Start - e, End = l.End - e }).ToList();
        return left.Count > 0 ? left : null;
    }

    [HarmonyPatch(typeof(SubtitlesView), nameof(SubtitlesView.Show))]
    public static class ReplayOnShow
    {
        static void Postfix(SubtitlesView __instance, ESubtitlesSource source)
        {
            try
            {
                var left = Remaining(source);
                if (left == null) return;
                var evt = new SubtitlesEvent { SubtitlesSource = source, CcData = left };
                __instance.method_0(evt);
            }
            catch (Exception e) { Plugin.Log.LogWarning("[subtitle] replaying the current line's subtitles failed: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(SubtitlesView), nameof(SubtitlesView.method_1))]
    public static class IgnoreStaleEnd
    {
        static bool Prefix(SubtitlesEndEvent endEvent)
        {
            if (endEvent == null || !IsStaleEnd(endEvent.SubtitlesSource)) return true;
            return false;
        }
    }
}
