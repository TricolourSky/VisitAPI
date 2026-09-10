using System.Collections.Generic;
using System.Linq;
using EFT.Quests;
using HarmonyLib;

namespace VisitAPI.Native;

/// <summary>任务 JSON 标 `"visitapi": { "anyOf": true }` 的，完成条件"任一达成"即可提交（引擎原生只有"全部达成"）。
/// 09-10 起 anyOf 也可以写成目标 id 数组＝「二选一组」：组内任一达成算组达成，组外的目标照旧全要
/// （SORA 的黑衣人任务：Killa 或 10 个 Scav 之后还得把情报中心建到 1 级才能交）。
/// 挂在 ConditionCollection.TestAll——Quest.CheckForStatusChange 判"是否可提交"就靠这一个函数。DEV_NOTES #67。</summary>
[HarmonyPatch(typeof(ConditionCollection), nameof(ConditionCollection.TestAll), typeof(IConditional))]
public static class AnyOfQuest
{
    static bool Prefix(ConditionCollection __instance, IConditional conditional, ref bool __result)
    {
        if (!(conditional is Quest quest) || quest.Template?.Conditions == null) return true;
        var group = QuestFlags.AnyOfGroup(quest.Id);
        if (group == null && !QuestFlags.AnyOf(quest.Id)) return true;
        if (!quest.Template.Conditions.TryGetValue(EQuestStatus.AvailableForFinish, out var finish) || !ReferenceEquals(finish, __instance)) return true;
        // 条件没登记 checker（作者配置残缺）时当没达成——裸索引会把异常抛进引擎的状态推进，整条任务卡死
        bool Pass(Condition c) => quest.ProgressCheckers.TryGetValue(c, out var pc) && pc.Test();
        var all = __instance.EarlyFinisherConditions.ToList();
        var members = group == null ? all : all.Where(c => group.Contains((string)c.id)).ToList();   // true = 所有目标都在组里（老行为）
        if (members.Count == 0) return true;   // 组里的 id 一条都对不上（写错了）：退回原生「全部达成」，别让任务白送或永远交不了
        __result = members.Any(Pass) && all.Except(members).All(Pass);
        return false;
    }

    /// 组里任一条达成 = 组达成。CompletedConditions 只在引擎推进状态时补记，任务进行中要再问一次 checker
    internal static bool GroupDone(Quest quest, List<string> group)
    {
        if (quest.Template?.Conditions == null || !quest.Template.Conditions.TryGetValue(EQuestStatus.AvailableForFinish, out var finish)) return false;
        return finish.Any(c => group.Contains((string)c.id) && (quest.CompletedConditions.Contains(c.id) || (quest.ProgressCheckers.TryGetValue(c, out var pc) && pc.Test())));
    }
}

/// <summary>「二选一组」的显示条件：目标的 visibilityConditions 指向组内某一条时，组达成就算门开了。
/// 原生 CheckVisibilityStatus 是逐条 AND（有一条没完成就不显示），做不出「Killa 或 Scav 任一完成后再冒出情报中心」。
/// 只接管带组的任务，其余一律放行原生；章节页（ChapterModel）和任务书用的是同一个函数，一处补两边都对。</summary>
[HarmonyPatch(typeof(Conditional<Quest>), nameof(Conditional<Quest>.CheckVisibilityStatus))]
public static class AnyOfVisibility
{
    static bool Prefix(object __instance, Condition condition, ref bool __result)
    {
        if (!(__instance is Quest quest) || condition?.VisibilityConditions == null || condition.VisibilityConditions.Length == 0) return true;
        var group = QuestFlags.AnyOfGroup(quest.Id);
        if (group == null) return true;
        try
        {
            var groupDone = AnyOfQuest.GroupDone(quest, group);
            foreach (var v in condition.VisibilityConditions)
            {
                var target = (v as ConditionOneTarget)?.target;
                if (target != null && group.Contains(target) && groupDone) continue;   // 指向组内：组达成即可
                if (v is ConditionCounterCreator || quest.IsConditionDone(v) || (target != null && quest.CompletedConditions.Contains(target))) continue;   // 其余照原生
                __result = false;
                return false;
            }
            __result = true;
        }
        catch (System.Exception) { __result = false; }   // 原生同样把异常吞成「不显示」
        return false;
    }
}
