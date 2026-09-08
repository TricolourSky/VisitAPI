using System.Linq;
using EFT.Quests;
using HarmonyLib;

namespace VisitAPI.Native;

/// <summary>任务 JSON 标 `"visitapi": { "anyOf": true }` 的，完成条件"任一达成"即可提交（引擎原生只有"全部达成"）。
/// 挂在 ConditionCollection.TestAll——Quest.CheckForStatusChange 判"是否可提交"就靠这一个函数。DEV_NOTES #67。</summary>
[HarmonyPatch(typeof(ConditionCollection), nameof(ConditionCollection.TestAll), typeof(IConditional))]
public static class AnyOfQuest
{
    static bool Prefix(ConditionCollection __instance, IConditional conditional, ref bool __result)
    {
        if (!(conditional is Quest quest) || quest.Template?.Conditions == null || !QuestFlags.AnyOf(quest.Id)) return true;
        if (!quest.Template.Conditions.TryGetValue(EQuestStatus.AvailableForFinish, out var finish) || !ReferenceEquals(finish, __instance)) return true;
        // 条件没登记 checker（作者配置残缺）时当没达成——裸索引会把异常抛进引擎的状态推进，整条任务卡死
        __result = __instance.EarlyFinisherConditions.Any(c => quest.ProgressCheckers.TryGetValue(c, out var pc) && pc.Test());
        return false;
    }
}
