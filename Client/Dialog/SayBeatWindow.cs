using EFT.Dialogs;
using EFT.UI;
using HarmonyLib;

namespace VisitAPI.Native;

/// <summary>10-02（SORA：点对话选项后，对话框有几帧先缩成一个小框再变回正常大小）：.dlg 的每个节点在引擎里是两段——
/// 「商人说话」（#npc，只有一句商人台词）和「玩家选项」（#opt）。原生窗口每换一段就重画：先把选项行全收掉，
/// 只有新一段的第一行是玩家台词时才重新摆选项。说话那一段没有玩家台词，于是窗口空着只剩一小条；
/// 正式版里商人这时在念台词，要念好几秒，我们的台词没有动画，下一帧就跳到选项段，看起来就是「小框闪一下」。
/// 这里让窗口跳过说话那一段的重画：对话进行中就原样留着上一组选项（先锁住不让点），刚打开或刚从旁白回来、一行选项都没有时先藏起来；
/// 选项段到了原生 Redraw 会自己显示并摆好。</summary>
[HarmonyPatch(typeof(TraderDialogWindow), nameof(TraderDialogWindow.Redraw))]
public static class SayBeatWindow
{
    static bool Prefix(TraderDialogWindow __instance, BaseTraderDialog dialog)
    {
        try
        {
            if (dialog == null || !DialogTemplateBuilder.SayDialogs.Contains(dialog.Id)) return true;
            if (!AnyRow(__instance)) __instance.HideGameObject();
            else if (__instance._optionsCanvasGroup != null)
            {
                __instance._optionsCanvasGroup.interactable = false;
                __instance._optionsCanvasGroup.blocksRaycasts = false;
            }
            return false;
        }
        catch { return true; }
    }

    static bool AnyRow(TraderDialogWindow window)
    {
        var rows = window._linesContainer;
        if (rows == null) return false;
        for (var i = 0; i < rows.childCount; i++)
            if (rows.GetChild(i).gameObject.activeSelf) return true;
        return false;
    }
}
