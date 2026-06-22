using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace VisitAPI.Native
{
    internal static class NativeBinder
    {
        internal static Type? TraderDialogScreenType;
        internal static Type? BtrDialogType;
        internal static ConstructorInfo? BtrDialogCtor;
        internal static Type? DialogControllerType;
        internal static ConstructorInfo? DialogControllerCtor;
        internal static MethodInfo? ShowScreenMethod;
        internal static Type? ScreenStateType;
        internal static object? ScreenStateQueued;
        internal static MethodInfo? Method5;

        internal static FieldInfo? ScreenControllerField;
        internal static MethodInfo? CloseScreenMethod;
        internal static object? ActiveController;
        internal static object? ActiveQuestController;
        internal static string ActiveTraderId = "";
        internal static string ActiveProfileId = "";
        // The live profile + the live out-of-raid trade screen (TraderScreensGroup) behind a menu-opened dialog.
        // Set on open; the menu path reads them off the trade screen, the raid path off GamePlayerOwner. Used for
        // {playerName}/standing/when-root resolution out of raid, and to switch the trade screen's tab on @trade/@tasks.
        internal static object? ActiveProfile;
        internal static object? ActiveTradeScreen;

        // EFT.UI.TraderScreensGroup (the out-of-raid Trade/Tasks/Services screen). It already holds every controller
        // we need to open a dialog out of raid, and method_3 switches its tab. method_6 (per-trader select) is the
        // entry-button patch target. All names are obfuscation-fragile → resolved here with a self-test.
        internal static Type? TraderScreensGroupType;
        internal static MethodInfo? TsgMethod6;
        internal static MethodInfo? TsgMethod3;
        internal static Type? TraderModeEnumType;
        internal static PropertyInfo? TsgTraderProp;
        internal static PropertyInfo? TsgProfileProp;
        internal static PropertyInfo? TsgInvProp;
        internal static PropertyInfo? TsgQuestProp;
        internal static FieldInfo? TsgCloseButtonField;
        private static FieldInfo? _traderIdField;

        private static PropertyInfo? _playerProfileProp;
        private static PropertyInfo? _playerInvProp;
        private static MemberInfo? _playerQuestCtrlMember;
        private static FieldInfo? _screenMongoIdField;

        internal static bool Ready { get; private set; }

        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        internal static bool Bind()
        {
            Ready = false;

            TraderDialogScreenType = AccessTools.TypeByName("EFT.UI.TraderDialogScreen");
            BtrDialogType = TraderDialogScreenType?.GetNestedType("BTRDialogClass", BindingFlags.Public | BindingFlags.NonPublic);
            BtrDialogCtor = BtrDialogType?.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(c => c.GetParameters().Length == 7);

            DialogControllerType = AccessTools.TypeByName("GClass3619") ?? AccessTools.TypeByName("GClass3618") ?? AccessTools.TypeByName("GClass3617");
            DialogControllerCtor = DialogControllerType?.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(c => c.GetParameters().Length == 3);

            ShowScreenMethod = BtrDialogType?.GetMethods(All)
                .FirstOrDefault(m => m.Name == "ShowScreen" && m.GetParameters().Length == 1);

            // Native close: TraderDialogScreen stores its controller (our BTRDialogClass) in the protected
            // BaseScreen.ScreenController field, and the dialog's own "leave" (QuitAction) closes via
            // ScreenController.CloseScreen() — the manager-driven top-down close that pops the screen stack
            // and restores cursor/input. We do the same instead of hiding the GameObject ourselves.
            ScreenControllerField = FindFieldUp(TraderDialogScreenType, "ScreenController");
            CloseScreenMethod = FindMethodUp(BtrDialogType, "CloseScreen");

            ScreenStateType = AccessTools.TypeByName("EFT.UI.Screens.EScreenState") ?? AccessTools.TypeByName("EScreenState");
            if (ScreenStateType != null && ScreenStateType.IsEnum && Enum.IsDefined(ScreenStateType, "Queued"))
                ScreenStateQueued = Enum.Parse(ScreenStateType, "Queued");

            Method5 = TraderDialogScreenType?.GetMethod("method_5", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _screenMongoIdField = TraderDialogScreenType?.GetField("mongoID_0", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            Type? playerType = AccessTools.TypeByName("EFT.Player");
            _playerProfileProp = playerType?.GetProperty("Profile", All);
            _playerInvProp = playerType?.GetProperty("InventoryController", All);
            _playerQuestCtrlMember = (MemberInfo?)playerType?.GetProperty("AbstractQuestControllerClass", All)
                ?? playerType?.GetField("AbstractQuestControllerClass", All);

            TraderScreensGroupType = AccessTools.TypeByName("EFT.UI.TraderScreensGroup");
            TsgMethod6 = TraderScreensGroupType?.GetMethods(All).FirstOrDefault(m => m.Name == "method_6" && m.GetParameters().Length == 1);
            TsgMethod3 = TraderScreensGroupType?.GetMethods(All).FirstOrDefault(m => m.Name == "method_3" && m.GetParameters().Length == 1);
            TraderModeEnumType = TraderScreensGroupType?.GetNestedType("ETraderMode", BindingFlags.Public | BindingFlags.NonPublic);
            TsgTraderProp = TraderScreensGroupType?.GetProperty("TraderClass", All);
            TsgProfileProp = TraderScreensGroupType?.GetProperty("Profile_0", All);
            TsgInvProp = TraderScreensGroupType?.GetProperty("InventoryController_0", All);
            TsgQuestProp = TraderScreensGroupType?.GetProperty("AbstractQuestControllerClass", All);
            TsgCloseButtonField = TraderScreensGroupType?.GetField("_closeButton", All);
            _traderIdField = AccessTools.TypeByName("TraderClass")?.GetField("Id", All);

            Ready = BtrDialogCtor != null && DialogControllerCtor != null && ShowScreenMethod != null && ScreenStateQueued != null;
            LogSelfTest();
            return Ready;
        }

        // Closes the active dialog the native way (manager top-down). Returns true if it invoked CloseScreen.
        internal static bool CloseActiveController(object? screen)
        {
            object? controller = ActiveController
                ?? (screen != null ? ScreenControllerField?.GetValue(screen) : null);
            ActiveController = null;
            if (controller == null || CloseScreenMethod == null) return false;
            try
            {
                CloseScreenMethod.Invoke(controller, null);
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[NativeBinder] CloseScreen: " + (ex.InnerException ?? ex).Message);
                return false;
            }
        }

        private static FieldInfo? FindFieldUp(Type? t, string name)
        {
            while (t != null && t != typeof(object))
            {
                FieldInfo? f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        private static MethodInfo? FindMethodUp(Type? t, string name)
        {
            while (t != null && t != typeof(object))
            {
                MethodInfo? m = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (m != null) return m;
                t = t.BaseType;
            }
            return null;
        }

        internal static object? GetProfile(object player) => _playerProfileProp?.GetValue(player);

        internal static string GetProfileId(object? profile)
        {
            if (profile == null) return "";
            try
            {
                const BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                Type t = profile.GetType();
                object? id = t.GetProperty("Id", f)?.GetValue(profile) ?? t.GetField("Id", f)?.GetValue(profile);
                return id as string ?? "";
            }
            catch { return ""; }
        }
        internal static object? GetInventoryController(object player) => _playerInvProp?.GetValue(player);

        internal static object? GetQuestController(object player)
        {
            return _playerQuestCtrlMember switch
            {
                PropertyInfo p => p.GetValue(player),
                FieldInfo f => f.GetValue(player),
                _ => null,
            };
        }

        internal static string? GetScreenTraderId(object screen)
        {
            object? mongoId = _screenMongoIdField?.GetValue(screen);
            if (mongoId == null) return null;
            Type t = mongoId.GetType();
            MethodInfo? conv = t.GetMethod("op_Implicit", BindingFlags.Static | BindingFlags.Public, null, new[] { t }, null)
                ?? t.GetMethod("op_Explicit", BindingFlags.Static | BindingFlags.Public, null, new[] { t }, null);
            if (conv != null && conv.ReturnType == typeof(string))
                return conv.Invoke(null, new[] { mongoId }) as string;
            return mongoId.ToString();
        }

        // --- Out-of-raid (trade screen) helpers ---

        internal static string GetTraderId(object traderClass)
        {
            try { return _traderIdField?.GetValue(traderClass) as string ?? ""; }
            catch { return ""; }
        }

        internal static object? GetTsgProfile(object screen) => TsgProfileProp?.GetValue(screen);
        internal static object? GetTsgInventory(object screen) => TsgInvProp?.GetValue(screen);
        internal static object? GetTsgQuestController(object screen) => TsgQuestProp?.GetValue(screen);
        internal static object? GetTsgTrader(object screen) => TsgTraderProp?.GetValue(screen);

        // Switch the live out-of-raid trade screen to Trade / Tasks / Services (method_3 also populates the tab).
        // Returns false when there is no trade screen behind the dialog (e.g. opened in raid) so the caller can no-op.
        internal static bool SwitchTradeTab(string modeName)
        {
            object? screen = ActiveTradeScreen;
            if (screen == null || TsgMethod3 == null || TraderModeEnumType == null) return false;
            try
            {
                object mode = Enum.Parse(TraderModeEnumType, modeName);
                TsgMethod3.Invoke(screen, new[] { mode });
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[NativeBinder] SwitchTradeTab: " + (ex.InnerException ?? ex).Message);
                return false;
            }
        }

        // Player level for `when: level>=N` root selection. Reads Profile.Info.Level; 0 if unavailable.
        internal static int GetPlayerLevel(object? profile)
        {
            if (profile == null) return 0;
            try
            {
                object? info = profile.GetType().GetProperty("Info", All)?.GetValue(profile);
                object? lvl = info?.GetType().GetProperty("Level", All)?.GetValue(info);
                return lvl != null ? Convert.ToInt32(lvl) : 0;
            }
            catch { return 0; }
        }

        // Trader standing (好感度) for `when: standing>=N` root selection. Reads Profile.TradersInfo[id].Standing.
        internal static double GetTraderStanding(object? profile, string traderId)
        {
            if (profile == null || string.IsNullOrEmpty(traderId)) return 0.0;
            try
            {
                object? infos = profile.GetType().GetProperty("TradersInfo", All)?.GetValue(profile)
                    ?? profile.GetType().GetField("TradersInfo", All)?.GetValue(profile);
                if (!(infos is IDictionary dict)) return 0.0;
                foreach (DictionaryEntry e in dict)
                {
                    if (!string.Equals(e.Key?.ToString(), traderId, StringComparison.OrdinalIgnoreCase)) continue;
                    object? st = e.Value?.GetType().GetProperty("Standing", All)?.GetValue(e.Value);
                    return st != null ? Convert.ToDouble(st) : 0.0;
                }
                return 0.0;
            }
            catch { return 0.0; }
        }

        private static void LogSelfTest()
        {
            void Line(string name, bool ok) => Plugin.Log.LogInfo($"[NativeBinder] {(ok ? "PASS" : "FAIL")}  {name}");
            Line("TraderDialogScreen", TraderDialogScreenType != null);
            Line("BTRDialogClass", BtrDialogType != null);
            Line("BTRDialogClass ctor(7)", BtrDialogCtor != null);
            Line($"DialogController ({DialogControllerType?.Name ?? "?"})", DialogControllerType != null);
            Line("DialogController ctor(3)", DialogControllerCtor != null);
            Line("ShowScreen(EScreenState)", ShowScreenMethod != null);
            Line("BaseScreen.ScreenController", ScreenControllerField != null);
            Line("Controller.CloseScreen()", CloseScreenMethod != null);
            Line("EScreenState.Queued", ScreenStateQueued != null);
            Line("method_5", Method5 != null);
            Line("TraderDialogScreen.mongoID_0", _screenMongoIdField != null);
            Line("Player.Profile", _playerProfileProp != null);
            Line("Player.InventoryController", _playerInvProp != null);
            Line("Player.AbstractQuestControllerClass", _playerQuestCtrlMember != null);
            // Phase 6 (out-of-raid entry) — not part of Ready; the entry button degrades gracefully if any FAIL.
            Line("TraderScreensGroup", TraderScreensGroupType != null);
            Line("TraderScreensGroup.method_6", TsgMethod6 != null);
            Line("TraderScreensGroup.method_3", TsgMethod3 != null);
            Line("TraderScreensGroup.ETraderMode", TraderModeEnumType != null);
            Line("TraderScreensGroup.TraderClass/Profile_0/Inv/Quest", TsgTraderProp != null && TsgProfileProp != null && TsgInvProp != null && TsgQuestProp != null);
            Line("TraderScreensGroup._closeButton", TsgCloseButtonField != null);
            Line("TraderClass.Id", _traderIdField != null);
            Plugin.Log.LogInfo($"[NativeBinder] Ready = {Ready}");
        }
    }
}
