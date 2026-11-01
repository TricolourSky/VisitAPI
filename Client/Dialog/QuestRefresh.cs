using System.Collections;
using System.Collections.Generic;
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
        // 10-16：`-> 商人id:节点` 跳去的别家剧本，它的选项门控用到的任务也一起盯着（劫持那段在 Abyss 的剧本里接任务）。
        // 10-24：直接拿这次开对话一起注册进来的整组剧本（DialogTemplateBuilder.Group），多级跳转（A→B→C）也在里面
        var trees = new List<DialogTree> { tree };
        foreach (var id in DialogTemplateBuilder.Group)
        {
            var other = DialogTemplateBuilder.Linked(id);
            if (other != null && !trees.Contains(other)) trees.Add(other);
        }
        var ids = trees.SelectMany(t => t.Nodes.Values).SelectMany(n => n.Options)
            .SelectMany(o => o.AcceptIds.Concat(o.CompleteIds).Concat(new[] { o.HandoverId, o.IfQuestId, o.IfNotQuestId }))
            .Where(id => id != null).Distinct().ToArray();
        if (ids.Length > 0) Plugin.Instance.StartCoroutine(Loop(dc, quests, ids));
    }

    static IEnumerator Loop(BaseTraderDialogController dc, QuestController quests, string[] ids)
    {
        yield return UiWait.Until(() => dc.CurrentDialog != null, 300);
        var last = new int[ids.Length]; var now = new int[ids.Length];
        Snapshot(quests, ids, last);
        while (dc.CurrentDialog != null)
        {
            yield return null;
            Snapshot(quests, ids, now);
            if (Same(now, last)) continue;
            var dialog = dc.CurrentDialog;
            if (dialog == null || dialog.DialogSide != EDialogSide.Player || dialog.IsBlocked) continue;
            System.Array.Copy(now, last, now.Length);
            DialogPage.Redraw(dc);
        }
    }

    static void Snapshot(QuestController quests, string[] ids, int[] into)
    {
        var book = quests.Quests;
        for (var i = 0; i < ids.Length; i++) into[i] = (int?)book?.GetConditional(ids[i])?.QuestStatus ?? -1;
    }

    static bool Same(int[] a, int[] b)
    {
        for (var i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }
}
