using System.Collections.Generic;
using System.Linq;
using EFT.Quests;
using HarmonyLib;

namespace VisitAPI.Native;

[HarmonyPatch(typeof(ConditionCollection), nameof(ConditionCollection.TestAll), typeof(IConditional))]
public static class AnyOfQuest
{
    static bool Prefix(ConditionCollection __instance, IConditional conditional, ref bool __result)
    {
        if (!(conditional is Quest quest) || quest.Template?.Conditions == null) return true;
        if (!quest.Template.Conditions.TryGetValue(EQuestStatus.AvailableForFinish, out var finish) || !ReferenceEquals(finish, __instance)) return true;
        bool Pass(Condition c) => quest.ProgressCheckers.TryGetValue(c, out var pc) && pc.Test();
        var finishers = QuestFlags.IsChapter(quest.Id) ? QuestFlags.Finishers(quest.Id) : null;
        if (finishers != null && finishers.Count > 0)
        {
            var fin = finish.OfType<ConditionQuest>().Where(c => finishers.Contains(c.target)).ToList();
            if (fin.Count > 0) { __result = fin.Any(Pass); return false; }
        }
        var group = QuestFlags.AnyOfGroup(quest.Id);
        if (group == null && !QuestFlags.AnyOf(quest.Id)) return true;
        var necessary = __instance.EarlyFinisherConditions.ToList();
        var members = group == null ? necessary : quest.ProgressCheckers.Keys.Where(c => group.Contains((string)c.id)).ToList();
        if (members.Count == 0) return true;
        __result = members.Any(Pass) && necessary.Except(members).All(Pass);
        return false;
    }

    internal static bool GroupDone(Quest quest, List<string> group)
    {
        if (quest.Template?.Conditions == null || !quest.Template.Conditions.TryGetValue(EQuestStatus.AvailableForFinish, out var finish)) return false;
        return finish.Any(c => group.Contains((string)c.id) && (quest.CompletedConditions.Contains(c.id) || (quest.ProgressCheckers.TryGetValue(c, out var pc) && pc.Test())));
    }
}

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
                if (target != null && group.Contains(target) && groupDone) continue;
                if (v is ConditionCounterCreator || quest.IsConditionDone(v) || (target != null && quest.CompletedConditions.Contains(target))) continue;
                __result = false;
                return false;
            }
            __result = true;
        }
        catch (System.Exception) { __result = false; }
        return false;
    }
}
