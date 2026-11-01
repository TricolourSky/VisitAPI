using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace VisitAPI.Native;

/// <summary>10-16 SORA 实机：「偶遇敌人的时候他们会找掩体而不是冲上去打架」——那是 SAIN 按兵种抽的性格。护卫一激活就把 SAIN 性格改成 GigaChad
/// （SAIN 里最敢推的那档），和 SPT-PitFireTeam 给队友改性格一个路数：全反射，不引用 SAIN 的程序集；SAIN 没装 / 版本变了就原样留着、警告一次。
/// SAIN 的 BotComponent 比激活回调晚一拍才建，所以隔一秒重试，最多 15 秒。</summary>
public static class EscortSain
{
    static Type _enable, _personality, _component;
    static MethodInfo _get;
    static bool _apiMissing;

    public static IEnumerator Aggressive(BotOwner bot)
    {
        if (!Installed) yield break;
        if (SainSkips(bot)) yield break;   // 10-25 夜：SAIN 按设置不管这种兵（比如开了 Vanilla Rogues），本来就用原版 AI，不用改性格也不用提示
        var deadline = Time.unscaledTime + 15f;
        while (Time.unscaledTime < deadline)
        {
            if (bot == null || bot.IsDead || _apiMissing) yield break;
            if (TryApply(bot)) yield break;
            yield return new WaitForSeconds(1f);
        }
        Plugin.WarnOnce("escort-sain-late", "[escort] SAIN never attached to an escort; it keeps SAIN's default personality");
    }

    /// true = 改好了（或者没法改、不用再试）；false = SAIN 那边还没建好，下一秒再来
    static bool TryApply(BotOwner bot)
    {
        try
        {
            _personality ??= AccessTools.TypeByName("SAIN.Preset.Shared.Models.Preset.Personalities.EPersonality");
            if (_personality == null) return Missing("EPersonality");
            var sain = Component(bot);
            if (sain == null) return false;
            var info = AccessTools.Property(sain.GetType(), "Info")?.GetValue(sain);
            if (info == null) return false;
            var set = AccessTools.Method(info.GetType(), "SetPersonality", new[] { _personality });
            if (set == null) return Missing("SAINBotInfoClass.SetPersonality");
            set.Invoke(info, new[] { Enum.Parse(_personality, "GigaChad") });
            AccessTools.Method(info.GetType(), "CalcTimeBeforeSearch", Type.EmptyTypes)?.Invoke(info, null);
            AccessTools.Method(info.GetType(), "CalcHoldGroundDelay", Type.EmptyTypes)?.Invoke(info, null);
            return true;
        }
        catch (Exception e)
        {
            _apiMissing = true;
            Plugin.WarnOnce("escort-sain-fail", "[escort] could not set the escorts' SAIN personality (SAIN version changed?): " + e.GetBaseException().Message);
            return true;
        }
    }

    static Type Enable => _enable ??= AccessTools.TypeByName("SAIN.SAINEnableClass") ?? AccessTools.TypeByName("SAIN.Plugin.SAINEnableClass");

    /// 这只兵的 SAIN BotComponent；SAIN 没管它（或者还没建好）是 null
    static object Component(BotOwner bot)
    {
        var enable = Enable;
        _component ??= AccessTools.TypeByName("SAIN.Components.BotComponent");
        object sain = null;
        if (enable != null)
        {
            // 4.5.1 只有 GetSAIN(string profileId, out BotComponent)；老版还有 GetSAIN(BotOwner)。两种都认
            _get ??= enable.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GetSAIN" && (m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(BotOwner)
                                                           || m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(string) && m.GetParameters()[1].IsOut));
            if (_get != null)
            {
                if (_get.GetParameters().Length == 1) sain = _get.Invoke(null, new object[] { bot });
                else { var args = new object[] { bot.ProfileId, null }; if (_get.Invoke(null, args) is true) sain = args[1]; }
            }
        }
        if (sain == null && _component != null) sain = bot.gameObject.GetComponent(_component);   // 退路：SAIN 的 BotComponent 挂在兵的物体上（BotComponentBase 是 MonoBehaviour）
        return sain;
    }

    static bool? _installed;
    static bool Installed => _installed ??= BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("me.sol.sain");
    static PropertyInfo _activeLayer;
    static object _noLayer;
    static bool _releaseBroken;

    /// 10-26（SORA 10-25 夜实机「杀完人之后就呆住了」）：我们的层（跟随 / 突入）接手时，把 SAIN 记的「当前层」清成 None。
    /// SAIN 只在评估它自己的层时才清这个记号；我们的层优先级高，它的层轮不到评估，记号一直开着，
    /// SAIN 的 BotMover 补丁见它开着就停掉原版走路器（交火时它自己控移动），我们发的路没人走，兵原地呆住。没装 SAIN / SAIN 不管这只就什么都不做
    public static void ReleaseLayer(BotOwner bot)
    {
        if (!Installed || _releaseBroken || bot == null) return;
        try
        {
            var sain = Component(bot);
            if (sain == null) return;
            _activeLayer ??= AccessTools.Property(sain.GetType(), "ActiveLayer");
            if (_activeLayer == null || !_activeLayer.CanWrite || !_activeLayer.PropertyType.IsEnum) throw new MissingMemberException(sain.GetType().FullName, "ActiveLayer");
            _noLayer ??= Enum.Parse(_activeLayer.PropertyType, "None");
            _activeLayer.SetValue(sain, _noLayer, null);
        }
        catch (Exception e)
        {
            _releaseBroken = true;
            Plugin.WarnOnce("escort-sain-layer", "[escort] could not tell SAIN that its layers are idle (SAIN version changed?); escorts may stand still after a fight: " + e.GetBaseException().Message);
        }
    }

    static MethodInfo _skips;

    /// SAIN 自己判这只兵归不归它管（SAINEnableClass.IsSAINDisabledForBot，SAIN 对每只兵都调它）；找不到这个方法就当它管
    static bool SainSkips(BotOwner bot)
    {
        try
        {
            _skips ??= Enable is Type enable ? AccessTools.Method(enable, "IsSAINDisabledForBot", new[] { typeof(BotOwner) }) : null;
            return _skips?.Invoke(null, new object[] { bot }) is true;
        }
        catch { return false; }
    }

    static bool Missing(string what)
    {
        _apiMissing = true;
        Plugin.WarnOnce("escort-sain-api", $"[escort] SAIN API not found ({what}); escorts keep SAIN's default personality");
        return true;
    }
}

/// <summary>10-17：SAIN 的撤离层（优先级 24，受伤 / 时间到 / 捡够了就跑撤离点）对护卫永远不激活——跟随层让位给 SAIN 打架的那几秒里它也能插进来，
/// 一跑就是半张图。按名字找 SAIN 的类型，不引用它的程序集；没装 SAIN（Prepare 为 false）就不挂。</summary>
[HarmonyPatch]
public static class EscortNoExtract
{
    static Type _layer;
    static bool _layerTried;   // 按名字找类型要扫一遍全部程序集：Prepare / TargetMethod 共用一次的结果，没装 SAIN 的 null 也记住
    static Type Layer
    {
        get
        {
            if (!_layerTried) { _layer = AccessTools.TypeByName("SAIN.Layers.ExtractLayer"); _layerTried = true; }
            return _layer;
        }
    }
    static PropertyInfo _botOwner;   // BigBrain CustomLayer.BotOwner；这个文件不引用 BigBrain 的类型，反射取一次缓存

    static bool Prepare() => Layer != null;

    static MethodBase TargetMethod() => AccessTools.Method(Layer, "IsActive") ?? throw new MissingMethodException("SAIN.Layers.ExtractLayer", "IsActive");

    /// 每只 SAIN 兵每次评估层都会进来：这局没有护卫就什么都不做
    static bool Prefix(object __instance, ref bool __result)
    {
        if (EscortRegistry.Count == 0) return true;
        try
        {
            _botOwner ??= AccessTools.Property(__instance.GetType(), "BotOwner");
            if (_botOwner?.GetValue(__instance, null) is not BotOwner bot || !EscortRegistry.IsEscortBot(bot)) return true;
        }
        catch { return true; }
        __result = false;
        return false;
    }
}
