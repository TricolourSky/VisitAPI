using System;
using EFT.Dialogs;

namespace VisitAPI.Native;

/// <summary>对话页的三个常用动作，都走原生换页的同一条路：method_0 按对话 id 重建一页，SetCurrentDialog 换上去。</summary>
public static class DialogPage
{
    /// <summary>按当前对话的 id 重画一页：选项门控变了、上交 / 关键抉择收尾都靠它。
    /// playerOnly = 只在当前页是玩家选项页时重画（商人说话 / 旁白那一页重建会把台词再播一遍）。</summary>
    public static void Redraw(BaseTraderDialogController dc, bool playerOnly = false)
    {
        var cur = dc?.CurrentDialog;
        if (cur == null) return;
        if (playerOnly && cur.DialogSide != EDialogSide.Player) return;
        dc.SetCurrentDialog(dc.method_0(cur.Id));
    }

    /// <summary>窗口（上交 / 关键抉择）取消以后：解锁当前页再重画，玩家能接着点别的选项。失败只记一条日志，prefix 是调用方的 [handover] / [choice]。</summary>
    public static void Reopen(BaseTraderDialogController dc, string prefix)
    {
        try
        {
            var cur = dc?.CurrentDialog;
            if (cur == null) return;
            cur.IsBlocked = false;
            dc.SetCurrentDialog(dc.method_0(cur.Id));
        }
        catch (Exception e) { Plugin.Log.LogWarning(prefix + " Failed to unblock after cancel: " + e.Message); }
    }

    /// <summary>被我们的前缀拦下的那一句，窗口里选完 / 点了「是」再原样执行一遍（前缀认出重入就放行）。
    /// failure 给失败时的日志；onDone 不管成败都调（上交窗用它收掉没用上的选择）。</summary>
    public static async void ExecuteAgain(ClientDialogController dc, BaseTraderDialogLine line, Func<Exception, string> failure, Action onDone = null)
    {
        try { await dc.ExecuteLine(line); }
        catch (Exception e) { Plugin.Log.LogWarning(failure(e)); }
        onDone?.Invoke();
    }
}
