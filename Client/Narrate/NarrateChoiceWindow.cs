using System;
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
            if (!ChoiceWindow.Show(key, () => { _confirmed = line; Run(dc, line); }, () => Cancel(dc, id)))
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

    static async void Run(ClientDialogController dc, BaseTraderDialogLine line)
    {
        try
        {
            await dc.ExecuteLine(line);
        }
        catch (Exception e) { Plugin.Log.LogWarning("[choice] Failed to execute line after confirmation: " + e); }
    }

    static void Cancel(ClientDialogController dc, string id)
    {
        try
        {
            var cur = dc?.CurrentDialog;
            if (cur == null) return;
            cur.IsBlocked = false;
            dc.SetCurrentDialog(dc.method_0(cur.Id));
        }
        catch (Exception e) { Plugin.Log.LogWarning("[choice] Failed to unblock after cancel: " + e.Message); }
    }

    public static void Reset()
    {
        _confirmed = null;
        ChoiceWindow.Close();
    }
}
