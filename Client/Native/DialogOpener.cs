using System;

namespace VisitAPI.Native
{
    internal static class DialogOpener
    {
        // Raid / hideout entry: controllers come from the local player (GamePlayerOwner.MyPlayer).
        internal static bool TryOpen(string traderId, out string error)
        {
            error = "";
            if (!NativeBinder.Ready)
            {
                error = "NativeBinder not ready";
                return false;
            }

            EFT.Player player = EFT.GamePlayerOwner.MyPlayer;
            if (player == null)
            {
                error = "GamePlayerOwner.MyPlayer is null (not in raid/hideout?)";
                return false;
            }

            object? profile = NativeBinder.GetProfile(player);
            object? questCtrl = NativeBinder.GetQuestController(player);
            object? invCtrl = NativeBinder.GetInventoryController(player);
            // Raid/hideout: there is no out-of-raid trade screen behind us, so @trade/@tasks no-op cleanly.
            NativeBinder.ActiveTradeScreen = null;
            return Open(traderId, profile, questCtrl, invCtrl, out error);
        }

        // Out-of-raid entry (menu trade screen): the caller supplies the controllers it already holds
        // (TraderScreensGroup.Profile_0 / AbstractQuestControllerClass / InventoryController_0). MyPlayer is null here.
        internal static bool TryOpenOutOfRaid(string traderId, object? profile, object? questCtrl, object? invCtrl, out string error)
        {
            error = "";
            if (!NativeBinder.Ready)
            {
                error = "NativeBinder not ready";
                return false;
            }
            return Open(traderId, profile, questCtrl, invCtrl, out error);
        }

        private static bool Open(string traderId, object? profile, object? questCtrl, object? invCtrl, out string error)
        {
            error = "";
            Plugin.Log.LogInfo($"[DialogOpener] controllers profile={profile != null} quest={questCtrl != null} inv={invCtrl != null}");
            if (profile == null || invCtrl == null)
            {
                error = $"profile/inventory missing (profile={profile != null} inv={invCtrl != null})";
                return false;
            }

            object dialogController;
            try
            {
                dialogController = NativeBinder.DialogControllerCtor!.Invoke(new object?[] { profile, questCtrl, invCtrl });
            }
            catch (Exception ex)
            {
                error = "dialogController ctor: " + (ex.InnerException ?? ex).Message;
                Plugin.Log.LogError("[DialogOpener] " + (ex.InnerException ?? ex));
                return false;
            }

            object btrDialog;
            try
            {
                btrDialog = NativeBinder.BtrDialogCtor!.Invoke(new object?[] { profile, traderId, questCtrl, invCtrl, null, dialogController, null });
            }
            catch (Exception ex)
            {
                error = "BTRDialogClass ctor: " + (ex.InnerException ?? ex).Message;
                Plugin.Log.LogError("[DialogOpener] " + (ex.InnerException ?? ex));
                return false;
            }

            // Native open: push the screen through EFT's screen manager. The manager runs PrepareEnvironment
            // (input/framerate/chatbar) and honors the screen's ShouldLockCursor()=>ShowCursor, so the cursor,
            // input-blocking and ESC are all handled by the game itself. We keep the controller so we can close
            // it the same way the native dialog does (ScreenController.CloseScreen()), which pops the manager
            // cleanly and returns to raid.
            try
            {
                NativeBinder.ShowScreenMethod!.Invoke(btrDialog, new object[] { NativeBinder.ScreenStateQueued! });
            }
            catch (Exception ex)
            {
                error = "ShowScreen: " + (ex.InnerException ?? ex).Message;
                Plugin.Log.LogError("[DialogOpener] " + (ex.InnerException ?? ex));
                return false;
            }

            NativeBinder.ActiveController = btrDialog;
            NativeBinder.ActiveQuestController = questCtrl;
            NativeBinder.ActiveProfile = profile;
            NativeBinder.ActiveTraderId = traderId;
            NativeBinder.ActiveProfileId = NativeBinder.GetProfileId(profile);
            Plugin.Log.LogInfo($"[DialogOpener] ShowScreen OK for trader {traderId} (quest controller {(questCtrl != null ? "present" : "NULL")})");
            return true;
        }
    }
}
