using System;
using System.Linq;
using System.Threading.Tasks;
using EFT.Dialogs;
using HarmonyLib;

namespace VisitAPI.Native;

[HarmonyPatch(typeof(ClientDialogController), nameof(ClientDialogController.ExecuteLine))]
public static class NarrateChoiceWindow
{
    static BaseTraderDialogLine _confirmed;

    [HarmonyPriority(Priority.First)]
    static bool Prefix(ClientDialogController __instance, BaseTraderDialogLine line, ref Task __result)
    {
        try
        {
            if (line == null || ReferenceEquals(line, _confirmed)) return true;
            if (line.DialogSide != EDialogSide.Player) return true;
            var id = line.Template?.Id.ToString();
            var key = DialogConfirm.KeyFor(id);
            if (key == null) return true;
            var dc = __instance;
            // 「是」：这句再执行一遍（上面认出 _confirmed 放行）
            if (!ChoiceWindow.Show(key, () => { _confirmed = line; DialogPage.ExecuteAgain(dc, line, e => "[choice] Failed to execute line after confirmation: " + e); }, () => No(dc, id)))
                return true;
            __result = Task.CompletedTask;
            return false;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[choice] Confirmation window takeover failed, executing this line natively: " + e.Message);
            return true;
        }
    }

    /// 「否」：.dlg 的 `confirm: 键名 -> 节点` 写了去处就跳过去——走原生换页的同一条路（method_0 建对话 + SetCurrentDialog，
    /// 见 BaseTraderDialogController 处理 DialogSwitchDialogAction 那段）；没写照 1.1 退回选项页（DialogPage.Reopen）。
    /// 10-17 SORA 实机「关键抉择选否卡在对话框里」：节点的入口是「商人说话 / 旁白」那一页（#npc / #nar0），只有一句 NPC 台词、靠执行这句才换到选项页。
    /// 原生换页之后会紧接着执行新页的第一句 NPC 台词（ClientDialogController.ExecuteLine 末尾、method_8），这里照原生补上这一步——
    /// 只换页不执行的话台词不播、选项页永远不来，SayBeatWindow 还把没有选项的窗藏了，对话就停在一个空窗上
    static void No(ClientDialogController dc, string id)
    {
        var target = DialogConfirm.NoTarget(id);
        if (target == null) { DialogPage.Reopen(dc, "[choice]"); return; }
        try
        {
            var cur = dc?.CurrentDialog;
            if (cur != null) cur.IsBlocked = false;
            dc.SetCurrentDialog(dc.method_0(target.Value));
            var now = dc.CurrentDialog;
            if (now != null && now.DialogSide == EDialogSide.Npc)
            {
                dc.LastNpcLine = now.Lines.FirstOrDefault();
                dc.LastNpcLine?.Execute();
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[choice] Failed to jump after \"No\", returning to the options page instead: " + e.Message);
            DialogPage.Reopen(dc, "[choice]");
        }
    }

    public static void Reset()
    {
        _confirmed = null;
        ChoiceWindow.Close();
    }
}
