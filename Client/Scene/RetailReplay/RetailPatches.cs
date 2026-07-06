using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using EFT;
using HarmonyLib;

namespace VisitAPI.Scene.RetailReplay
{
    // The 3 patches the retail dialog replay needs on SPT (the method_5 whitelist bypass lives in
    // Native/WhitelistPatch, shared with the .dlg path). All target the NATIVE template engine, which only
    // the retail replay drives — .dlg dialogs never execute these code paths.
    internal static class RetailPatches
    {
        internal static void Apply(Harmony harmony)
        {
            MethodInfo? method13 = typeof(GClass3619).GetMethod("method_13", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method13 != null)
            {
                harmony.Patch(method13, prefix: new HarmonyMethod(typeof(RetailPatches).GetMethod(nameof(SkipDialogProgressSync), BindingFlags.Static | BindingFlags.NonPublic)));
                Plugin.Log.LogInfo("[RetailPatches] dialog-progress network sync skipped (method_13)");
            }
            else
            {
                Plugin.Log.LogWarning("[RetailPatches] GClass3619.method_13 not found — SPT server may force-quit the dialog on the first line");
            }

            MethodInfo? randomTest = typeof(GClass3664).GetMethod(nameof(GClass3664.Test), BindingFlags.Instance | BindingFlags.Public);
            if (randomTest != null)
            {
                harmony.Patch(randomTest, prefix: new HarmonyMethod(typeof(RetailPatches).GetMethod(nameof(RandomWindowInclusiveEnd), BindingFlags.Static | BindingFlags.NonPublic)));
                Plugin.Log.LogInfo("[RetailPatches] rotation dead-roll fix installed (Random window end inclusive)");
            }
            else
            {
                Plugin.Log.LogWarning("[RetailPatches] GClass3664.Test not found — greetings will occasionally dead-roll into the red fallback row");
            }

            MethodInfo? questStatusTest = typeof(GClass3659).GetMethod(nameof(GClass3659.Test), BindingFlags.Instance | BindingFlags.Public);
            if (questStatusTest != null)
            {
                harmony.Patch(questStatusTest, prefix: new HarmonyMethod(typeof(RetailPatches).GetMethod(nameof(QuestStatusMissingAsLocked), BindingFlags.Static | BindingFlags.NonPublic)));
                Plugin.Log.LogInfo("[RetailPatches] QuestStatus fallback installed (missing quest = Locked)");
            }
            else
            {
                Plugin.Log.LogWarning("[RetailPatches] GClass3659.Test not found — quest-gated greetings will stay dead");
            }
        }

        // Every executed dialog line runs a dialog-progress operation: it applies the line LOCALLY
        // (execute its actions, advance to the next dialog — SetDialogProgress) and POSTs it to the server.
        // The SPT 4.0.13 server doesn't know the request and the failure path force-quits the dialog, so we
        // keep the local apply and drop only the network leg. Skipping the whole method made every click a
        // no-op — options never advanced and the quit line did nothing.
        private static bool SkipDialogProgressSync(GClass3619 __instance, MongoID traderId, MongoID dialogId, GClass3625 line, ref Task __result)
        {
            try
            {
                GStruct155 result = __instance.SetDialogProgress(traderId, dialogId, line.Template.Id);
                if (result.Failed)
                    Plugin.Log.LogWarning("[RetailPatches] dialog progress rejected: " + result.Error);
            }
            catch (Exception ex)
            {
                // A throw here (a line jumps to a template missing from the extract) leaves CurrentDialog
                // on the NPC side, and the caller (method_12) then re-executes its first NPC line — same
                // throw, forever, on the SHARED controller. Kill the dialog instead: null the state so the
                // chain dies, then raise the quit action so the screen and scene close cleanly.
                Plugin.Log.LogWarning("[RetailPatches] local dialog progress: " + ex.Message + " — ending dialog");
                try { __instance.StopDialog(); } catch { }
                __instance.method_10(new GClass3643());
            }
            __result = Task.CompletedTask;
            return false;
        }

        // Greeting rotation variants carry window conditions like 0-99/100-199/…/MaxValue=400 (Ragman).
        // The native test is end-EXCLUSIVE (`roll < EndValue`) while the roll spans 0..MaxValue INCLUSIVE,
        // so 99/199/299/399/400 match NO window — the whole greeting group dies and the dialog builder
        // falls back to a single red "Back" row (~1 in 80 opens; a live-EFT bug, not ours). Treat EndValue
        // as inclusive and clamp the roll under MaxValue so every roll lands in a window; the next window
        // starts at previousEnd+1, so inclusive ends never double-match.
        private static bool RandomWindowInclusiveEnd(GClass3664 __instance, GInterface460 context, ref bool __result)
        {
            int roll = context.GetRandomValue(__instance);
            if (__instance.MaxValue > 0 && roll >= (int)__instance.MaxValue) roll = (int)__instance.MaxValue - 1;
            __result = roll >= (int)__instance.StartValue && roll <= (int)__instance.EndValue;
            return false;
        }

        // The retail dialogs gate lines on QuestStatus of 1.0 quests the SPT database doesn't have; the
        // native test returns FALSE for a quest missing from the profile, which killed every one of
        // Prapor's greeting lines (each ANDs two such quests) and left his dialog dead at the entry state.
        // A quest the profile has never seen is by definition still Locked — evaluate it that way and only
        // hand known quests to the native comparison.
        private static bool QuestStatusMissingAsLocked(GClass3659 __instance, GInterface460 context, ref bool __result)
        {
            System.Collections.Generic.IEnumerable<QuestDataClass>? quests = context?.QuestsData;
            if (quests == null)
            {
                __result = false;
                return false;
            }
            foreach (QuestDataClass quest in quests)
                if (quest.Id == __instance.QuestId) return true;
            __result = __instance.Statuses != null && __instance.Statuses.Contains(EFT.Quests.EQuestStatus.Locked);
            return false;
        }
    }
}
