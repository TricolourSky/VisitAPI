using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.Dialogs;
using EFT.Quests;
using VisitAPI.Dialog;

namespace VisitAPI.Native;

public static class QuestGates
{
    public static DialogAction[] Actions(DialogOption o, DialogAction main)
    {
        var acts = new List<DialogAction>();
        // accept:/complete: 可以一次带多个任务，一个动作一条
        foreach (var id in o.AcceptIds) acts.Add(new DialogAcceptQuestAction(id));
        foreach (var id in o.CompleteIds) acts.Add(new DialogFinishQuestAction { QuestId = id });
        // 记号先写再跳转：下一屏的 ifvar: 门控读的就是这一笔
        if (o.SetVarName != null) acts.Add(new DialogSetVariableAction(new DialogSetVariableAction.SaveStateData(VariableService.Id(o.SetVarName), o.SetVarValue, DialogLineTemplate.ESaveStateType.Profile)));
        if (main != null) acts.Add(main);
        return acts.Count > 0 ? acts.ToArray() : null;
    }

    public static DialogMainConditionGroup Trigger(DialogOption o, QuestController quests, DialogCondition extra = null)
    {
        var conds = new List<DialogCondition>();
        if (o.IfQuestId != null) conds.Add(new QuestGate(quests, o.IfQuestId, o.IfStatuses, false));
        if (o.IfNotQuestId != null) conds.Add(new QuestGate(quests, o.IfNotQuestId, o.IfNotStatuses, true));
        if (o.IfVarName != null) conds.Add(new VariableValueCondition(VariableService.Id(o.IfVarName), o.IfVarValue));
        // `ifitems` 是**追加**的一条，不参与"没写任何条件就自动补一条"的判断 ——
        // 它的语义是"在原有门控之上再要求背包里有东西可交"，不是替代品
        // 只认 `ifitems: 任务` 明写的、或同选项 handover: 的那条。**不能拿 complete: 兜底** ——
        // 任务能交的时候物品条件早就做完了，ItemsGate 恒 false，那个选项会永远不出现（最难查的一类症状）
        var itemsFor = o.IfItems ? (o.IfItemsId ?? o.HandoverId) : null;
        if (conds.Count == 0 && !o.Always)
        {
            // 多任务时每个都要到位（子组内条件是"且"）
            if (o.AcceptIds.Count > 0) foreach (var id in o.AcceptIds) conds.Add(new QuestGate(quests, id, new List<int> { (int)EQuestStatus.AvailableForStart }, false));
            else if (o.HandoverId != null) conds.Add(new QuestGate(quests, o.HandoverId, new List<int> { (int)EQuestStatus.Started }, false));
            else if (o.CompleteIds.Count > 0) foreach (var id in o.CompleteIds) conds.Add(new QuestGate(quests, id, new List<int> { (int)EQuestStatus.AvailableForFinish }, false));
        }
        if (itemsFor != null) conds.Add(new ItemsGate(quests, itemsFor));
        if (extra != null) conds.Add(extra);
        return conds.Count == 0 ? null : new DialogMainConditionGroup(new[] { new DialogConditionSubGroup(conds) });
    }

}
