using System.Collections.Generic;
using EFT;
using EFT.Dialogs;
using EFT.Quests;

namespace VisitAPI.Native;

public static class SetStatusService
{
    static readonly Dictionary<MongoID, (string quest, int status)> _byLine = new();

    public static void Register(MongoID lineId, string questId, int status) => _byLine[lineId] = (questId, status);

    public static void Watch(BaseTraderDialogController dc, QuestController quests)
    {
        dc.OnDialogChanged += dialog =>
        {
            if (dialog == null) return;
            dialog.OnExecuteLine += line =>
            {
                if (line?.Template != null && _byLine.TryGetValue(line.Template.Id, out var s)) Apply(quests, s.quest, s.status);
            };
        };
    }

    static void Apply(QuestController quests, string questId, int status)
    {
        var quest = quests?.Quests?.GetConditional(questId);
        if (quest == null) { Plugin.Log.LogWarning("[setstatus] quest not found: " + questId); return; }
        if (!quests.TryExecuteTransition(quest, (EQuestStatus)status)) quests.SetConditionalStatus(quest, (EQuestStatus)status);
        Plugin.Log.LogDebug($"[setstatus] {questId} -> {(EQuestStatus)status} (now {quest.QuestStatus})");
    }
}
