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
        if (o.AcceptId != null) acts.Add(new DialogAcceptQuestAction(o.AcceptId));
        if (o.CompleteId != null) acts.Add(new DialogFinishQuestAction { QuestId = o.CompleteId });
        if (main != null) acts.Add(main);
        return acts.Count > 0 ? acts.ToArray() : null;
    }

    public static DialogMainConditionGroup Trigger(DialogOption o, QuestController quests, DialogCondition extra = null)
    {
        var conds = new List<DialogCondition>();
        if (o.IfQuestId != null) conds.Add(new QuestGate(quests, o.IfQuestId, o.IfStatuses, false));
        if (o.IfNotQuestId != null) conds.Add(new QuestGate(quests, o.IfNotQuestId, o.IfNotStatuses, true));
        if (conds.Count == 0 && !o.Always)
        {
            if (o.AcceptId != null) conds.Add(new QuestGate(quests, o.AcceptId, new List<int> { (int)EQuestStatus.AvailableForStart }, false));
            else if (o.HandoverId != null) conds.Add(new QuestGate(quests, o.HandoverId, new List<int> { (int)EQuestStatus.Started }, false));
            else if (o.CompleteId != null) conds.Add(new QuestGate(quests, o.CompleteId, new List<int> { (int)EQuestStatus.AvailableForFinish }, false));
        }
        if (extra != null) conds.Add(extra);
        return conds.Count == 0 ? null : new DialogMainConditionGroup(new[] { new DialogConditionSubGroup(conds) });
    }

}
