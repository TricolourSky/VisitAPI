using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.Dialogs;
using EFT.Quests;
using VisitAPI.Dialog;

namespace VisitAPI.Native;

public class QuestGate : DialogCondition
{
    readonly QuestController _quests;
    readonly string _questId;
    readonly List<int> _statuses;
    readonly bool _not;

    public QuestGate(QuestController quests, string questId, List<int> statuses, bool not)
    { _quests = quests; _questId = questId; _statuses = statuses; _not = not; }

    public override EDialogConditionType Type => EDialogConditionType.QuestStatus;

    public override bool Test(IDialogContext context)
    {
        var quest = _quests?.Quests?.GetConditional(_questId);
        var status = quest != null ? (int)quest.QuestStatus : 0;
        return _not ? !_statuses.Contains(status) : _statuses.Contains(status);
    }
}

public class ItemsGate : DialogCondition
{
    readonly QuestController _quests;
    readonly string _questId;

    public ItemsGate(QuestController quests, string questId) { _quests = quests; _questId = questId; }

    public override EDialogConditionType Type => EDialogConditionType.QuestStatus;

    public override bool Test(IDialogContext context)
    {
        var cond = QuestGates.PendingItems(_quests?.Quests?.GetConditional(_questId));
        if (cond == null) return false;
        var items = _quests.GetItemsForCondition(cond);
        return items != null && items.Length > 0;
    }
}

public static class QuestGates
{
    public static ConditionItem PendingItems(Quest quest) =>
        quest?.ProgressCheckers?.Keys.Where(quest.CheckVisibilityStatus).OfType<ConditionItem>().FirstOrDefault(c => !quest.IsConditionDone(c));

    public static DialogAction[] Actions(DialogOption o, DialogAction main)
    {
        var acts = new List<DialogAction>();
        foreach (var id in o.AcceptIds) acts.Add(new DialogAcceptQuestAction(id));
        foreach (var id in o.CompleteIds) acts.Add(new DialogFinishQuestAction { QuestId = id });
        if (o.SetVarName != null) acts.Add(new DialogSetVariableAction(new DialogSetVariableAction.SaveStateData(Vars.Id(o.SetVarName), o.SetVarValue, DialogLineTemplate.ESaveStateType.Profile)));
        if (main != null) acts.Add(main);
        return acts.Count > 0 ? acts.ToArray() : null;
    }

    public static DialogMainConditionGroup Trigger(DialogOption o, QuestController quests, DialogCondition extra = null)
    {
        var conds = new List<DialogCondition>();
        if (o.IfQuestId != null) conds.Add(new QuestGate(quests, o.IfQuestId, o.IfStatuses, false));
        if (o.IfNotQuestId != null) conds.Add(new QuestGate(quests, o.IfNotQuestId, o.IfNotStatuses, true));
        if (o.IfVarName != null) conds.Add(new VariableValueCondition(Vars.Id(o.IfVarName), o.IfVarValue));
        var itemsFor = o.IfItems ? (o.IfItemsId ?? o.HandoverId) : null;
        if (conds.Count == 0 && !o.Always)
        {
            if (o.AcceptIds.Count > 0) foreach (var id in o.AcceptIds) conds.Add(new QuestGate(quests, id, new List<int> { (int)EQuestStatus.AvailableForStart }, false));
            else if (o.HandoverId != null) conds.Add(new QuestGate(quests, o.HandoverId, new List<int> { (int)EQuestStatus.Started }, false));
            else if (o.CompleteIds.Count > 0) foreach (var id in o.CompleteIds) conds.Add(new QuestGate(quests, id, new List<int> { (int)EQuestStatus.AvailableForFinish }, false));
        }
        if (itemsFor != null) conds.Add(new ItemsGate(quests, itemsFor));
        if (extra != null) conds.Add(extra);
        return conds.Count == 0 ? null : new DialogMainConditionGroup(new[] { new DialogConditionSubGroup(conds) });
    }
}
