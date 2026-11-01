using EFT;
using EFT.Dialogs;
using EFT.UI;
using HarmonyLib;

namespace VisitAPI.Native;

/// 10-16 SORA（通话被 Abyss 劫持）：`-> 商人id:节点` 跨剧本跳转后，对话窗左上的名字和头像要跟着换成说话的那家。
/// 原生 TraderDialogWindow.Show 只在开屏时按屏幕的商人设一次（名字 + 头像），之后 Redraw 不碰；
/// 这里按「当前对话模板属于哪家剧本」（DialogTemplateBuilder.TraderByDialog）补一下，跳回本家时也跟着换回来。
/// 原版商人的零售对话不在那张表里，一个字不动
public static class SpeakerSwitch
{
    static string _shown;
    // _dialogController 是 protected，没在公开化范围里，走 Harmony 反射取
    static readonly System.Reflection.FieldInfo Controller = AccessTools.Field(typeof(TraderDialogWindow), "_dialogController");

    [HarmonyPatch(typeof(TraderDialogWindow), nameof(TraderDialogWindow.Show))]
    public static class Shown
    {
        static void Postfix(Profile.TraderInfo traderInfo) { _shown = traderInfo?.Settings?.Id.ToString(); }
    }

    [HarmonyPatch(typeof(TraderDialogWindow), nameof(TraderDialogWindow.Redraw))]
    public static class Redrawn
    {
        static void Postfix(TraderDialogWindow __instance, BaseTraderDialog dialog)
        {
            try
            {
                if (dialog == null || !DialogTemplateBuilder.TraderByDialog.TryGetValue(dialog.Id, out var traderId) || traderId == _shown) return;
                var profile = (Controller?.GetValue(__instance) as IDialogContext)?.Profile;
                if (profile == null || !profile.TradersInfo.TryGetValue(new MongoID(traderId), out var info) || info?.Settings == null) return;
                if (__instance._traderName != null) __instance._traderName.text = info.Settings.Nickname.Localized();
                if (__instance._linesIcon != null) info.Settings.GetAndAssignAvatar(__instance._linesIcon, __instance.CancellationToken);
                _shown = traderId;
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("[dialog] Failed to switch the speaker's name and avatar: " + e.Message); }
        }
    }
}
