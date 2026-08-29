using System.Collections;
using System.Linq;
using EFT.Dialogs;
using EFT.Quests;
using VisitAPI.Dialog;

namespace VisitAPI.Native;

public static class QuestRefresh
{
    public static void Watch(BaseTraderDialogController dc, DialogTree tree, QuestController quests)
    {
        if (quests == null) return;
        var ids = tree.Nodes.Values.SelectMany(n => n.Options)
            .SelectMany(o => o.AcceptIds.Concat(o.CompleteIds).Concat(new[] { o.HandoverId, o.IfQuestId, o.IfNotQuestId }))
            .Where(id => id != null).Distinct().ToArray();
        if (ids.Length > 0) Plugin.Instance.StartCoroutine(Loop(dc, quests, ids));
    }

    static IEnumerator Loop(BaseTraderDialogController dc, QuestController quests, string[] ids)
    {
        for (var i = 0; i < 300 && dc.CurrentDialog == null; i++) yield return null;
        var last = Snapshot(quests, ids);
        while (dc.CurrentDialog != null)
        {
            yield return null;
            var now = Snapshot(quests, ids);
            if (now == last) continue;
            var dialog = dc.CurrentDialog;
            if (dialog == null || dialog.DialogSide != EDialogSide.Player || dialog.IsBlocked) continue;
            last = now;
            dc.SetCurrentDialog(dc.method_0(dialog.Id));
            Plugin.Log.LogDebug("[refresh] quest status changed, current node rebuilt");
        }
    }

    static string Snapshot(QuestController quests, string[] ids) =>
        string.Concat(ids.Select(id => ((int?)quests.Quests?.GetConditional(id)?.QuestStatus ?? -1).ToString()));
}
