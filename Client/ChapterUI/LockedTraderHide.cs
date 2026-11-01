using System;
using System.Collections.Generic;
using System.Linq;
using EFT.Trading;
using EFT.UI;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace VisitAPI.Native;

/// <summary>10-16 深夜 SORA：「过完了无线电拦截和街区那段 Abyss 才该出现」——包里注册的商人（服务端 TraderLoader 登记的那些，/visitapi/traders）
/// 没解锁就不出现在交易界面；原版对锁着的商人是灰卡（Jaeger 那样），对剧情商人等于提前剧透名字和头像。解锁那一刻（任务的 unlockTrader 奖励随
/// profileChanges.traderRelations 当场到客户端）再打开交易界面就有。别家的商人（AstralDivide 的 SORA、原版）照旧灰卡。
/// 商人列表有两条路：老式交易界面直接 new TraderScreenController(列表)；「交易中间页」设置打开时先过 MerchantsList.Show(列表) 再按一家开 TraderScreenController。
/// 两处都在参数上过滤，不碰只开一家的那条路（列表只有那一家时原样放行，免得 TradersList.First() 空）。</summary>
public static class LockedTraderHide
{
    static readonly HashSet<string> _pack = new(StringComparer.OrdinalIgnoreCase);

    public static void Fetch() => Plugin.Instance.StartCoroutine(VisitHttp.Fetch("/visitapi/traders", TryParse, "[traders]", _ => { }));

    static bool TryParse(string body)
    {
        try
        {
            if (JObject.Parse(body)["data"] is not JArray ids) return false;
            lock (_pack)
            {
                _pack.Clear();
                foreach (var id in ids) { var s = id.Value<string>(); if (!string.IsNullOrEmpty(s)) _pack.Add(s); }
            }
            return true;
        }
        catch (Exception e) { Plugin.Log.LogWarning("[traders] parse failed: " + e.Message); return false; }
    }

    public static bool IsPack(string traderId) { lock (_pack) return !string.IsNullOrEmpty(traderId) && _pack.Contains(traderId); }

    static bool Hidden(Trader t)
    {
        try { return t?.Info != null && !t.Info.Available && IsPack(t.Id.ToString()); }
        catch { return false; }
    }

    static IEnumerable<Trader> Filter(IEnumerable<Trader> list)
    {
        if (list == null) return null;
        var all = list.ToList();
        var kept = all.Where(t => !Hidden(t)).ToList();
        return kept.Count == 0 || kept.Count == all.Count ? list : kept;
    }

    /// 老式交易界面 / 中间页点进一家：TraderScreenController(trader, tradersList, …) 的列表
    [HarmonyPatch]
    public static class Screen
    {
        static System.Reflection.MethodBase TargetMethod() =>
            AccessTools.GetDeclaredConstructors(typeof(TraderScreensGroup.TraderScreenController)).FirstOrDefault(c => c.GetParameters().Length == 8)
            ?? throw new MissingMethodException(typeof(TraderScreensGroup.TraderScreenController).FullName, ".ctor(8 args)");

        static void Prefix(ref IEnumerable<Trader> tradersList)
        {
            try { tradersList = Filter(tradersList); }
            catch (Exception e) { Plugin.WarnOnce("trader-hide", "[trader] could not filter locked story traders out of the trader screen: " + e.Message); }
        }
    }

    /// 「交易中间页」的商人清单
    [HarmonyPatch]
    public static class Merchants
    {
        static System.Reflection.MethodBase TargetMethod() =>
            AccessTools.FirstMethod(typeof(MerchantsList), m => m.Name == "Show" && m.GetParameters().Length == 7)
            ?? throw new MissingMethodException(typeof(MerchantsList).FullName, "Show(7 args)");

        static void Prefix(ref IEnumerable<Trader> tradersList)
        {
            try { tradersList = Filter(tradersList); }
            catch (Exception e) { Plugin.WarnOnce("trader-hide-list", "[trader] could not filter locked story traders out of the merchants list: " + e.Message); }
        }
    }
}
