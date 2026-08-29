using System.Linq;
using EFT.Quests;
using HarmonyLib;

namespace VisitAPI.Native;

/// <summary>
/// 任务 JSON 标了 `"visitapi": { "anyOf": true }` 的，完成条件"任一达成"即可提交（引擎原生只有"全部达成"）。
/// 挂在 ConditionCollection.TestAll —— Quest.CheckForStatusChange 判"是否可提交"就靠这一个函数。见 DEV_NOTES #67。
/// </summary>
[HarmonyPatch(typeof(ConditionCollection), nameof(ConditionCollection.TestAll), typeof(IConditional))]
public static class AnyOfQuest
{
    static bool Prefix(ConditionCollection __instance, IConditional conditional, ref bool __result)
    {
        if (!(conditional is Quest quest) || quest.Template?.Conditions == null || !QuestFlags.AnyOf(quest.Id)) return true;
        if (!quest.Template.Conditions.TryGetValue(EQuestStatus.AvailableForFinish, out var finish) || !ReferenceEquals(finish, __instance)) return true;
        __result = __instance.EarlyFinisherConditions.Any(c => quest.ProgressCheckers[c].Test());
        return false;
    }
}
