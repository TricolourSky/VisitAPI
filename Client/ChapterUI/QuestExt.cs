using System.Collections.Generic;
using System.Linq;
using EFT.Quests;

namespace VisitAPI.Native;

/// <summary>任务模板条件的两个取法（10-24 收拢，旧 B7）。</summary>
public static class QuestExt
{
    /// 模板里某状态（AvailableForStart / AvailableForFinish / Fail…）的条件列表；任务 / 模板为空、或没这一档就给空序列
    public static IEnumerable<Condition> Conds(this Quest quest, EQuestStatus status)
    {
        var dict = quest?.Template?.Conditions;
        if (dict == null || !dict.TryGetValue(status, out var list) || list == null) return Enumerable.Empty<Condition>();
        return list;
    }

    /// 可接条件里的任务前置：ConditionQuest 点名的任务 id（空的跳过）
    public static IEnumerable<string> StartGates(this Quest quest) =>
        quest.Conds(EQuestStatus.AvailableForStart).OfType<ConditionQuest>().Select(c => c.target).Where(t => !string.IsNullOrEmpty(t));
}
