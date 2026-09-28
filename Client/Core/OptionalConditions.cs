using System;
using System.Collections.Generic;
using EFT.Quests;
using HarmonyLib;
using VisitAPI.ChapterUI;

namespace VisitAPI.Native;

/// <summary>09-25 SORA 实机：迷宫章节 Jaeger 任务（68e3a3d2）第一步「找到疗养院地下设施的入口通道」踩了区域、计数器到了 10，
/// 任务的 completedConditions 里却始终没有它，后面每一步（靠「前一步已完成」解锁显示）全部卡死，线索字条也一直被 QuestLootGate 藏着（09-26 1.1MCP 实测 1.1 不按目标可见性藏任务物品，QuestLootGate 已删，交回原生 ManageQuestLoot）。
/// 原因：0.16.9 的 Conditional.UpdateCompletedConditions 只记 ConditionCollection.GetCompletedConditionTemplates 返回的条件，
/// 而它只看 EarlyFinisherConditions = IsNecessary 的条件（IsNecessary => _isNecessary || 没有 ParentId）。
/// 1.1 的剧情任务大量用「非必需的子条件 + CompleteCondition 可见性链」（完不完成不影响交任务，但决定下一步显不显示），
/// 0.16 里这些子条件达标了也永远记不上。这里按 1.1 的行为补上：剧情任务里进度已达标的非必需条件，也并进「已完成」列表。
/// 是否能交任务仍由 TestAll（只看必需条件）决定，不受影响；只对 VisitAPI 内容包的剧情任务生效。</summary>
[HarmonyPatch(typeof(ConditionCollection), nameof(ConditionCollection.GetCompletedConditionTemplates))]
public static class OptionalConditions
{
    static void Postfix(ConditionCollection __instance, IConditional conditional, ref IEnumerable<Condition> __result)
    {
        try
        {
            if (conditional is not Quest quest || !QuestFlags.IsStory(quest.Id)) return;
            HashSet<Condition> merged = null;
            foreach (var c in __instance)
            {
                if (c == null || c.IsNecessary) continue;
                if (!conditional.ProgressCheckers.TryGetValue(c, out var checker) || checker == null || !checker.Test()) continue;
                merged ??= new HashSet<Condition>(__result ?? Array.Empty<Condition>());
                merged.Add(c);
            }
            if (merged != null) __result = merged;
        }
        catch (Exception e) { Plugin.Log.LogWarning("[quest] recording optional conditions failed (engine default kept): " + e.Message); }
    }
}
