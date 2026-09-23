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
            Plugin.Log.LogInfo($"[choice] 台词 {id} 是关键抉择（{key}），先弹确认窗");
            if (!ChoiceWindow.Show(key, () => { _confirmed = line; Run(dc, line); }, () => Cancel(dc, id)))
                return true;
            __result = Task.CompletedTask;
            return false;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[choice] 确认窗接管失败，这句照原生执行: " + e.Message);
            return true;
        }
    }

    static async void Run(ClientDialogController dc, BaseTraderDialogLine line)
    {
        try
        {
            await dc.ExecuteLine(line);
            Plugin.Log.LogInfo($"[choice] {line.Template?.Id} 执行完毕，当前对话页 {dc?.CurrentDialog?.Id}");
        }
        catch (Exception e) { Plugin.Log.LogWarning("[choice] 确认后执行台词失败: " + e); }
    }

    static void Cancel(ClientDialogController dc, string id)
    {
        try
        {
            var cur = dc?.CurrentDialog;
            if (cur == null) return;
            cur.IsBlocked = false;
            dc.SetCurrentDialog(dc.method_0(cur.Id));
            Plugin.Log.LogInfo($"[choice] {id} 玩家选了否，这句不执行，已解锁并重建选项页 {cur.Id}");
        }
        catch (Exception e) { Plugin.Log.LogWarning("[choice] 取消后解锁失败: " + e.Message); }
    }

    public static void Reset()
    {
        _confirmed = null;
        ChoiceWindow.Close();
    }
}
