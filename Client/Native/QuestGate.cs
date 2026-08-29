using System.Collections.Generic;
using EFT.Dialogs;
using EFT.Quests;

namespace VisitAPI.Native;

public class QuestGate : DialogCondition
{
    readonly QuestController _quests;
    readonly string _questId;
    readonly List<int> _statuses;
    readonly bool _not;

    public QuestGate(QuestController quests, string questId, List<int> statuses, bool not)
    {
        _quests = quests; _questId = questId; _statuses = statuses; _not = not;
    }

    public override EDialogConditionType Type => EDialogConditionType.QuestStatus;

    public override bool Test(IDialogContext context)
    {
        var quest = _quests?.Quests?.GetConditional(_questId);
        var status = quest != null ? (int)quest.QuestStatus : 0;
        return _not ? !_statuses.Contains(status) : _statuses.Contains(status);
    }
}
