using System.Collections.Generic;
using System.Reflection;
using EFT;
using HarmonyLib;

namespace VisitAPI.Native;

/// <summary>10-16 护卫永远不打真人：进 BotsGroup.Enemies 的唯一入口是 AddEnemy（初始分敌、被打复仇、MoreBots 阵营、
/// BotsController 的 AddEnemyToAllGroups* 全走它），SAIN 的敌人又只从 Enemies 生成（EnemyListController.getEnemyInfo）——
/// 所以护卫组里对真人一律拦下就够。IsPlayerEnemy 也拦一道，让构造里真人直接进 Neutrals，不用先进再退。</summary>
public static class EscortGuards
{
    /// 护卫组 + 真人 → 拦下（__result = false）；别的组、AI 都放行原生
    static bool Block(BotsGroup group, IPlayer person, ref bool result)
    {
        if (person == null || person.IsAI || !EscortRegistry.IsEscortGroup(group)) return true;
        result = false;
        return false;
    }

    [HarmonyPatch(typeof(BotsGroup), nameof(BotsGroup.AddEnemy))]
    public static class NoHumanEnemy
    {
        static bool Prefix(BotsGroup __instance, IPlayer person, ref bool __result) => Block(__instance, person, ref __result);
    }

    [HarmonyPatch(typeof(BotsGroup), nameof(BotsGroup.IsPlayerEnemy))]
    public static class HumanNotEnemy
    {
        static bool Prefix(BotsGroup __instance, IPlayer player, ref bool __result) => Block(__instance, player, ref __result);
    }

    /// <summary>10-25 纯 API：不装 SAIN 时护卫用的是兵种自己的原版大脑，里面有几层对护卫是添乱的，不让它们接手（10-25 反编译各大脑的层表核过）：
    /// PMC 大脑的撤离层（优先级 79，比跟随层高，到点就跑撤离点）、警告玩家（Scav 73 / Rogue 65：冲着主人喊话、拿枪指着）、
    /// Rogue 的和平请求（80，同样是对附近玩家的喊话）、Scav 响应别的 Scav 求援（75，会被拽去帮别人）、Scav 离图。
    /// 装了 SAIN 时这几层多半已被 SAIN 拿掉，挂着也不碍事</summary>
    [HarmonyPatch]
    public static class NativeLayers
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var t in new[] { typeof(ExfiltrationLayer), typeof(WarnPlayerLayer), typeof(ExUsecPeacefulRequestLayer), typeof(ForcedForHelpAssaultLayer), typeof(LeaveMapLayer) })
            {
                var m = AccessTools.DeclaredMethod(t, nameof(BaseLogicLayerSimple.ShallUseNow));
                if (m != null) yield return m;
            }
        }

        /// 每只兵每次评估这几层都会进来：这局没有护卫就什么都不做
        static void Postfix(BaseLogicLayerSimple __instance, ref bool __result)
        {
            if (__result && EscortRegistry.Count > 0 && EscortRegistry.IsEscortBot(__instance._owner)) __result = false;
        }
    }
}
