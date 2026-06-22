using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using SPT.Common.Http;
using UnityEngine;
using UnityEngine.UI;
using VisitAPI.Native;

namespace VisitAPI
{
    // Phase 4 (client, native-first): drive real native/WTT quests straight from .dlg options — accept /
    // complete / set-status — by reflecting the player's quest controller. This handles quests already in the
    // QuestBook (e.g. the WTT Ragman quest the SORA demo uses); server-side CreateQuest for hidden custom
    // quests is a later slice. CRITICAL (DEV_NOTES #2 / [[reference-unity-mainthread]]): the native
    // Accept/Finish/Handover return a Task whose ContinueWith runs OFF the main thread — those callbacks may
    // ONLY log, never drive UI/dialog. We fire the action synchronously from the option click (main thread).
    internal static class NativeQuestController
    {
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal static void AcceptQuest(string questId)
        {
            if (string.IsNullOrEmpty(questId)) return;
            Plugin.Log.LogInfo("[NativeQuest] AcceptQuest: " + questId);
            // Raid: the player's own quest controller. Hideout: none on the player, so use the menu/hideout's real
            // controller (Singleton<HideoutClass>) — the SAME one CompleteQuest finishes on — so a dialogue
            // `accept:` works in the hideout too and the task board refreshes live. See ResolveHideoutQuestController.
            object? ctrl = ResolveQuestController() ?? ResolveHideoutQuestController();
            Plugin.Log.LogInfo("[NativeQuest] accept controller: " + (ctrl?.GetType().Name ?? "NULL"));
            if (ctrl == null) { Plugin.Log.LogWarning("[NativeQuest] no quest controller (accept)"); return; }
            if (TryNativeAcceptQuest(ctrl, questId)) ReevaluateReadyQuests(questId);
        }

        // Returns true if the quest was completed (or queued to the server route); false if completion was
        // REFUSED because the quest still owes a hand-over. A dialogue `complete:` force-sets status→
        // AvailableForFinish then FinishQuest — which skips the real item check — so without this guard a partial
        // hand-over (e.g. 1/10 materials) would wrongly complete the quest. The check runs at option-click time,
        // well after the async HandoverItem has settled, so IsConditionDone reflects the true counter.
        internal static bool CompleteQuest(string questId)
        {
            if (string.IsNullOrEmpty(questId)) return false;
            Plugin.Log.LogInfo("[NativeQuest] CompleteQuest: " + questId);
            // Raid: the player's own quest controller. Hideout: none on the player, but the menu/hideout keeps
            // its OWN real controller (Singleton<HideoutClass>) — the one TasksScreen renders from. Complete on
            // THAT so its events refresh the task board live + grant XP, no restart. See ResolveHideoutQuestController.
            object? ctrl = ResolveQuestController() ?? ResolveHideoutQuestController();
            object? quest = ctrl != null ? FindQuestInBook(ctrl, questId) : null;
            if (quest != null && !AllHandoverConditionsDone(quest))
            {
                Plugin.Log.LogWarning("[NativeQuest] complete REFUSED for " + questId + ": hand-over conditions not yet satisfied");
                return false;
            }
            Plugin.Log.LogInfo("[NativeQuest] complete controller: " + (ctrl?.GetType().Name ?? "NULL"));
            if (ctrl != null && TryNativeCompleteQuest(ctrl, questId))
            {
                TryPlayQuestCompletedSound();
                ReevaluateReadyQuests(questId);
                return true;
            }
            // No controller / finish failed: fall back to the VisitAPI server route (completes + XP, no mail,
            // but the task UI only refreshes after the next profile reload).
            CompleteViaServer(questId);
            return true;
        }

        // True if every ConditionHandoverItem on the quest is satisfied (or the quest has none). Used to refuse a
        // premature `complete:` AND to decide whether a hand-over window session actually finished the objective.
        // A quest with no hand-over conditions (the device / kill / skill demos) passes vacuously, unchanged.
        private static bool AllHandoverConditionsDone(object quest)
        {
            Type? handoverType = AccessTools.TypeByName("EFT.Quests.ConditionHandoverItem");
            if (handoverType == null || !(quest.GetType().GetProperty("NecessaryConditions", All)?.GetValue(quest) is IEnumerable conds)) return true;
            MethodInfo? isDone = quest.GetType().GetMethods(All).FirstOrDefault(m => m.Name == "IsConditionDone" && m.GetParameters().Length == 1);
            if (isDone == null) return true;
            foreach (object cond in conds)
            {
                if (cond == null || !handoverType.IsInstanceOfType(cond)) continue;
                if (!(isDone.Invoke(quest, new[] { cond }) is bool b && b)) return false;
            }
            return true;
        }

        // Hideout / no-controller completion: the SPT server finishes the quest (status Success + XP) WITHOUT
        // sending trader mail (VisitAPI-Server VisitApiQuestRouter). We FIRST mark Success in the local profile
        // on the main thread — so the task list + the hideout visibility gate (both read Player.Profile.QuestsData)
        // reflect completion immediately — THEN persist it server-side off-thread. The HTTP callback only logs,
        // never drives UI, per [[reference-unity-mainthread]]; a failed server call simply reverts on next reload.
        private static void CompleteViaServer(string questId)
        {
            Plugin.Log.LogInfo("[NativeQuest] no controller — completing '" + questId + "' via VisitAPI server route");
            ProfileQuestStatus(questId, 4); // EQuestStatus.Success
            TryPlayQuestCompletedSound();
            Task.Run(() =>
            {
                try
                {
                    string resp = RequestHandler.GetJson("/visitapi/quest/complete");
                    Plugin.Log.LogInfo("[NativeQuest] server complete ok: " + (resp ?? "(null)"));
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning("[NativeQuest] server complete failed: " + (ex.InnerException?.Message ?? ex.Message));
                }
            });
        }

        // Hand over a quest's required items straight from a .dlg option (`handover: <questId>`). Mirrors EFT's
        // own quest-screen handover (EFT.UI.QuestObjectiveView): for each ConditionHandoverItem the quest still
        // needs, ask the controller which inventory items satisfy it (GetItemsForCondition) and drive native
        // HandoverItem(quest, condition, items, runNetworkTransaction:true). Using the real controller (raid or
        // the hideout singleton) means the server consumes the items + the task UI refreshes live. Handing over
        // only ADVANCES the handover condition — a separate `complete:` still finishes the quest.
        // ===== Native handover window — the player picks exact amounts; EFT's UI handles stack-splitting =====
        // Opens EFT.UI.HandoverQuestItemsWindow for each not-yet-done ConditionHandoverItem of the quest, one
        // after another, and submits the player-selected Item[] to the controller's HandoverItem. Async: the
        // dialog continues via onDone(true) only after the player works through every window. Ported from 0.2.4;
        // Show signature verified on SPT 4.0.13 (Condition, double, Item[], Profile, TraderControllerClass,
        // Action<Item[]>, bool). Falls back to the whole-stack auto path only if the window type is missing.
        internal static void HandoverItemViaWindow(string questId, string? label, Action<bool> onDone)
        {
            if (string.IsNullOrEmpty(questId)) { onDone(false); return; }
            Plugin.Log.LogInfo("[NativeQuest] HandoverItemViaWindow: " + questId);
            try
            {
                if (AccessTools.TypeByName("EFT.UI.HandoverQuestItemsWindow") == null)
                {
                    Plugin.Log.LogWarning("[NativeQuest] HandoverQuestItemsWindow missing — falling back to whole-stack auto handover");
                    HandoverItemAuto(questId); onDone(true); return;
                }
                object? ctrl = ResolveQuestController() ?? ResolveHideoutQuestController();
                object? quest = ctrl != null ? FindQuestInBook(ctrl, questId) : null;
                if (ctrl == null || quest == null) { Plugin.Log.LogWarning("[NativeQuest] handover: no controller/quest for " + questId); onDone(false); return; }

                List<object> conds = CollectPendingHandoverConditions(ctrl, quest);
                if (conds.Count == 0)
                {
                    // No window to open: either every hand-over is already satisfied (→ let the dialog proceed to
                    // its complete node) or the player carries NONE of the needed items (→ not satisfied, stay put).
                    bool done = AllHandoverConditionsDone(quest);
                    Plugin.Log.LogInfo("[NativeQuest] handover: no pending window for " + questId + " (allDone=" + done + ")");
                    onDone(done); return;
                }
                ChainHandoverWindows(ctrl, quest, questId, label, conds, 0, true, onDone);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[NativeQuest] handover window failed: " + (ex.InnerException?.Message ?? ex.Message));
                onDone(false);
            }
        }

        // The quest's ConditionHandoverItem conditions that are NOT yet satisfied AND the player actually has
        // items for (so we never pop an empty window or one for an already-completed objective).
        private static List<object> CollectPendingHandoverConditions(object ctrl, object quest)
        {
            List<object> result = new List<object>();
            Type? handoverType = AccessTools.TypeByName("EFT.Quests.ConditionHandoverItem");
            if (handoverType == null || !(quest.GetType().GetProperty("NecessaryConditions", All)?.GetValue(quest) is IEnumerable conds)) return result;
            MethodInfo? isDone = quest.GetType().GetMethods(All).FirstOrDefault(m => m.Name == "IsConditionDone" && m.GetParameters().Length == 1);
            MethodInfo? getItems = ctrl.GetType().GetMethods(All).FirstOrDefault(m => m.Name == "GetItemsForCondition" && m.GetParameters().Length == 1);
            foreach (object cond in conds)
            {
                if (cond == null || !handoverType.IsInstanceOfType(cond)) continue;
                if (isDone != null && isDone.Invoke(quest, new[] { cond }) is bool done && done) continue;
                if (getItems != null && getItems.Invoke(ctrl, new[] { cond }) is Array items && items.Length == 0) continue;
                result.Add(cond);
            }
            return result;
        }

        // Open the handover window for conds[index]; its accept callback opens the next one, until none remain.
        // allMet stays true only while every window's submitted amount covered that condition's required `value`,
        // so onDone(true) means the player actually handed over enough to finish — not merely that they clicked OK.
        private static void ChainHandoverWindows(object ctrl, object quest, string questId, string? label, List<object> conds, int index, bool allMet, Action<bool> onDone)
        {
            if (index >= conds.Count) { ReevaluateReadyQuests(questId); onDone(allMet); return; }
            bool opened = TryOpenHandoverWindow(ctrl, quest, questId, label, conds[index],
                condMet => ChainHandoverWindows(ctrl, quest, questId, label, conds, index + 1, allMet && condMet, onDone));
            if (!opened) onDone(false);
        }

        private static bool TryOpenHandoverWindow(object ctrl, object quest, string questId, string? label, object cond, Action<bool> onAccepted)
        {
            Type? windowType = AccessTools.TypeByName("EFT.UI.HandoverQuestItemsWindow");
            if (windowType == null) return false;
            MethodInfo? getItems = ctrl.GetType().GetMethods(All).FirstOrDefault(m => m.Name == "GetItemsForCondition" && m.GetParameters().Length == 1);
            object? eligible = getItems?.Invoke(ctrl, new[] { cond });
            if (!(eligible is Array arr) || arr.Length == 0) { Plugin.Log.LogInfo("[NativeQuest] handover: no eligible items for a condition of " + questId); return false; }
            object? profile = ctrl.GetType().GetField("Profile", All)?.GetValue(ctrl);
            object? invCtrl = ResolveInventoryController();
            if (profile == null || invCtrl == null) { Plugin.Log.LogWarning("[NativeQuest] handover: no profile/inventory controller"); return false; }
            MethodInfo? show = windowType.GetMethods(All).FirstOrDefault(m => m.Name == "Show" && m.GetParameters().Length == 7 && m.GetParameters()[0].ParameterType.Name == "Condition");
            if (show == null) { Plugin.Log.LogWarning("[NativeQuest] HandoverQuestItemsWindow.Show(7) not found"); return false; }
            Component? window = ResolveHandoverWindow(windowType);
            if (window == null) { Plugin.Log.LogWarning("[NativeQuest] no HandoverQuestItemsWindow instance/prefab"); return false; }
            MethodInfo? handover = ctrl.GetType().GetMethods(All).FirstOrDefault(m => m.Name == "HandoverItem" && m.GetParameters().Length == 4);
            // How many items were ALREADY handed over for this condition (the quest's progress checker). The window
            // shows "0 / (value - currentValue)" and caps selection at the remainder — passing 0 here is what made
            // it always show "0 / 10" and let the player re-hand-over the full amount each visit (over-handover).
            double currentValue = ReadConditionCurrentValue(quest, cond);

            // The window's accept delegate is Action<Item[]>; build it from the param type so we never have to
            // name EFT.InventoryLogic.Item. On accept, submit the player-selected items + chain to the next window.
            Type acceptType = show.GetParameters()[5].ParameterType;
            Type selType = acceptType.IsGenericType ? acceptType.GetGenericArguments()[0] : typeof(object);
            Action<object> onAcceptObj = selectedObj =>
            {
                bool met = false;
                try
                {
                    // The window passes exactly what the player chose to hand over (stacks already split to the
                    // selected amounts), so summing their counts == the amount being submitted now. Add what was
                    // already in (currentValue): met=false on a partial total keeps the dialog off the `complete:` node.
                    int required = ReadIntMember(cond, "value", 1);
                    int submitted = SumStackCounts(selectedObj as Array);
                    met = currentValue + submitted >= required;
                    if (handover != null && handover.Invoke(ctrl, new object[] { quest, cond, selectedObj, true }) is Task t)
                        t.ContinueWith(tt => Plugin.Log.LogInfo(tt.IsFaulted
                            ? "[NativeQuest] HandoverItem faulted: " + tt.Exception?.InnerException?.Message
                            : "[NativeQuest] HandoverItem completed: " + questId));
                    Plugin.Log.LogInfo("[NativeQuest] handover submitted for " + questId + " (+" + submitted + ", now " + (currentValue + submitted) + "/" + required + ", met=" + met + ")");
                }
                catch (Exception ex) { Plugin.Log.LogWarning("[NativeQuest] handover onAccept: " + (ex.InnerException?.Message ?? ex.Message)); }
                onAccepted(met);
            };
            ParameterExpression sp = Expression.Parameter(selType, "selected");
            Delegate typedAccept = Expression.Lambda(acceptType,
                Expression.Invoke(Expression.Constant(onAcceptObj), Expression.Convert(sp, typeof(object))), sp).Compile();

            window.gameObject.SetActive(true);
            show.Invoke(window, new object[] { cond, currentValue, eligible, profile, invCtrl, typedAccept, true });
            Canvas canvas = window.GetComponent<Canvas>() ?? window.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 2000;
            if (window.GetComponent<GraphicRaycaster>() == null) window.gameObject.AddComponent<GraphicRaycaster>();
            if (!string.IsNullOrEmpty(label)) OverrideWindowCaption(window, label!);
            Plugin.Log.LogInfo("[NativeQuest] HandoverQuestItemsWindow shown for " + questId);
            return true;
        }

        // The window titles itself "<上交给商人> (<items[0].ShortName>)" — with a multi-tpl "category" hand-over the
        // first eligible item (e.g. 螺栓) is misleading. Swap ONLY the parenthetical for the author's label, keeping
        // the localized prefix. The caption is the one TMP carrying a "(...)"; the count TMP shows "N / M", no paren.
        private static void OverrideWindowCaption(Component window, string label)
        {
            try
            {
                // TMPro isn't a direct assembly reference here, so resolve TMP_Text + its `text` property by reflection.
                Type? tmpType = AccessTools.TypeByName("TMPro.TMP_Text");
                PropertyInfo? textProp = tmpType?.GetProperty("text", All);
                if (textProp == null) return;
                foreach (Component t in window.GetComponentsInChildren(tmpType, true))
                {
                    if (t == null || !(textProp.GetValue(t) is string cur) || cur.Length == 0) continue;
                    int open = cur.LastIndexOf('(');
                    if (open < 0 || cur.IndexOf(')', open) < 0) continue;
                    textProp.SetValue(t, cur.Substring(0, open) + "(" + label + ")");
                    return;
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[NativeQuest] caption override: " + (ex.InnerException?.Message ?? ex.Message)); }
        }

        private static object? ResolveInventoryController()
        {
            EFT.Player p = EFT.GamePlayerOwner.MyPlayer;
            return p != null ? NativeBinder.GetInventoryController(p) : null;
        }

        // Prefer an existing scene instance of the window; otherwise instantiate the prefab under an active canvas.
        private static Component? ResolveHandoverWindow(Type windowType)
        {
            Component? scene = null, prefab = null;
            foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(windowType))
            {
                if (!(o is Component mb)) continue;
                if (mb.gameObject.scene.IsValid()) { scene = mb; break; }
                prefab = mb;
            }
            if (scene != null) { Plugin.Log.LogInfo("[NativeQuest] using existing HandoverQuestItemsWindow scene instance"); return scene; }
            if (prefab == null) return null;
            Transform? parent = FindActiveUiParent();
            if (parent == null) { Plugin.Log.LogWarning("[NativeQuest] no active UI parent for HandoverQuestItemsWindow"); return null; }
            GameObject go = UnityEngine.Object.Instantiate(prefab.gameObject, parent, false);
            return go.GetComponent(windowType) as Component;
        }

        private static Transform? FindActiveUiParent()
        {
            foreach (Canvas c in Resources.FindObjectsOfTypeAll<Canvas>())
                if (c != null && c.gameObject.scene.IsValid() && c.isActiveAndEnabled) return c.transform;
            return null;
        }

        // Fallback only (native window unavailable): whole-stack auto-grab — OVER-hands if the player carries a
        // bigger stack than required, so it's used solely when EFT.UI.HandoverQuestItemsWindow can't be resolved.
        private static void HandoverItemAuto(string questId)
        {
            if (string.IsNullOrEmpty(questId)) return;
            Plugin.Log.LogInfo("[NativeQuest] HandoverItemAuto (fallback): " + questId);
            object? ctrl = ResolveQuestController() ?? ResolveHideoutQuestController();
            if (ctrl == null) { Plugin.Log.LogWarning("[NativeQuest] no quest controller (handover)"); return; }
            object? quest = FindQuestInBook(ctrl, questId);
            if (quest == null) { Plugin.Log.LogInfo("[NativeQuest] " + questId + " not in QuestBook; skip handover"); return; }
            try
            {
                Type? handoverType = AccessTools.TypeByName("EFT.Quests.ConditionHandoverItem");
                if (!(quest.GetType().GetProperty("NecessaryConditions", All)?.GetValue(quest) is IEnumerable conds))
                {
                    Plugin.Log.LogWarning("[NativeQuest] handover: no NecessaryConditions on " + questId);
                    return;
                }
                MethodInfo? getItems = ctrl.GetType().GetMethods(All).FirstOrDefault(m => m.Name == "GetItemsForCondition" && m.GetParameters().Length == 1);
                MethodInfo? handover = ctrl.GetType().GetMethods(All).FirstOrDefault(m => m.Name == "HandoverItem" && m.GetParameters().Length == 4);
                if (getItems == null || handover == null) { Plugin.Log.LogWarning("[NativeQuest] handover: GetItemsForCondition/HandoverItem not found"); return; }

                int fired = 0;
                foreach (object cond in conds)
                {
                    if (cond == null || handoverType == null || !handoverType.IsInstanceOfType(cond)) continue;
                    if (!(getItems.Invoke(ctrl, new[] { cond }) is Array all) || all.Length == 0)
                    {
                        Plugin.Log.LogInfo("[NativeQuest] handover: no matching items in inventory for a condition of " + questId);
                        continue;
                    }
                    // CRITICAL: the native HandoverItem gives away the FULL stack count of EVERY item passed
                    // (EFT.Quests.ConditionHandoverItem.ConvertToHandoverItems uses item.StackObjectsCount with no
                    // cap, then the quest counter += the sum). GetItemsForCondition returns the player's ENTIRE
                    // matching inventory — so passing it straight through would over-hand-over. Pass only as many
                    // whole items/stacks as the condition's `value` needs. (Whole-stack granularity: a single stack
                    // that already covers the need is handed over IN FULL — we can't split a stack through this API.)
                    int required = ReadIntMember(cond, "value", 1);
                    Array items = TakeUpToCount(all, required, out int total);
                    if (items.Length == 0) continue;
                    if (total > required)
                        Plugin.Log.LogWarning("[NativeQuest] handover: stack overshoot — giving " + total + " for a need of " + required + " (whole-stack granularity, no split) on " + questId);

                    if (handover.Invoke(ctrl, new object[] { quest, cond, items, true }) is Task task)
                        task.ContinueWith(t => Plugin.Log.LogInfo(t.IsFaulted
                            ? "[NativeQuest] HandoverItem faulted: " + t.Exception?.InnerException?.Message
                            : "[NativeQuest] HandoverItem completed: " + questId));
                    Plugin.Log.LogInfo("[NativeQuest] native HandoverItem triggered: " + questId + " (" + items.Length + " stack(s) = " + total + " item(s), need " + required + ")");
                    fired++;
                }
                if (fired == 0) Plugin.Log.LogInfo("[NativeQuest] handover: no pending item-handover condition with items for " + questId);
                else ReevaluateReadyQuests(questId);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[NativeQuest] handover failed: " + (ex.InnerException?.Message ?? ex.Message));
            }
        }

        // Already-accumulated progress for a condition: QuestClass.ProgressCheckers[cond].CurrentValue (verified
        // against SPT/EFT QuestClass — IsConditionDone reads the same checker). Returns 0 if it can't be read or
        // the checker has no getter (so a fresh objective still opens with the full amount needed).
        private static double ReadConditionCurrentValue(object quest, object cond)
        {
            try
            {
                object? checkers = AccessTools.Property(quest.GetType(), "ProgressCheckers")?.GetValue(quest)
                    ?? AccessTools.Field(quest.GetType(), "ProgressCheckers")?.GetValue(quest);
                if (!(checkers is System.Collections.IDictionary dict) || !dict.Contains(cond)) return 0.0;
                object checker = dict[cond];
                if (checker == null) return 0.0;
                if (checker.GetType().GetMethod("HasGetter", All)?.Invoke(checker, null) is bool hasGetter && !hasGetter) return 0.0;
                object? cv = checker.GetType().GetProperty("CurrentValue", All)?.GetValue(checker)
                    ?? AccessTools.Field(checker.GetType(), "CurrentValue")?.GetValue(checker);
                return cv != null ? Convert.ToDouble(cv) : 0.0;
            }
            catch { return 0.0; }
        }

        // Sum StackObjectsCount across an Item[] (the player's selection from the hand-over window) — i.e. the
        // total number of items being handed over right now. Null/empty → 0.
        private static int SumStackCounts(Array? items)
        {
            if (items == null) return 0;
            int total = 0;
            foreach (object item in items)
                if (item != null) total += ReadIntMember(item, "StackObjectsCount", 1);
            return total;
        }

        // Read an int member (`value`, `StackObjectsCount`, …) off an EFT object by field OR property name,
        // falling back if it can't be read. AccessTools.Field walks the base-class chain (these live on bases).
        private static int ReadIntMember(object obj, string name, int fallback)
        {
            try
            {
                // Property first (`value`, `StackObjectsCount` are properties on these EFT types) — AccessTools.Field
                // logs a noisy warning when the name isn't a field, so only fall back to it if the property is absent.
                object? v = obj.GetType().GetProperty(name, All)?.GetValue(obj)
                    ?? AccessTools.Field(obj.GetType(), name)?.GetValue(obj);
                if (v != null) return Convert.ToInt32(v);
            }
            catch { }
            return fallback;
        }

        // Take whole items/stacks from `all` (an Item[]) until their StackObjectsCount sum reaches `required`,
        // returning a same-element-type array of just those items (so HandoverItem is never handed MORE stacks
        // than the quest needs) and outputting the summed item count. Whole-stack granularity — the final stack
        // can push `total` past `required`; splitting a stack isn't possible through HandoverItem(quest,cond,Item[]).
        private static Array TakeUpToCount(Array all, int required, out int total)
        {
            total = 0;
            Type elem = all.GetType().GetElementType() ?? typeof(object);
            List<object> picked = new List<object>();
            foreach (object item in all)
            {
                if (item == null) continue;
                picked.Add(item);
                total += ReadIntMember(item, "StackObjectsCount", 1);
                if (total >= required) break;
            }
            Array result = Array.CreateInstance(elem, picked.Count);
            for (int i = 0; i < picked.Count; i++) result.SetValue(picked[i], i);
            return result;
        }

        internal static void SetQuestStatus(string questId, int status) => TryNativeSetQuestStatus(questId, status, true);

        // Live status (0–5) of a quest, or null if it can't be read. Tries the native quest controller first
        // (raid), then falls back to the PROFILE quest list (Player.Profile.QuestsData) — the controller does
        // not exist in the hideout, but the profile does, so this is how hideout gating reads quest state.
        internal static int? GetQuestStatus(string questId)
        {
            if (string.IsNullOrEmpty(questId)) return null;
            try
            {
                object? ctrl = ResolveQuestController() ?? ResolveHideoutQuestController();
                object? quest = ctrl != null ? FindQuestInBook(ctrl, questId) : null;
                int? st = quest != null ? ReadQuestStatus(quest) : null;
                if (st.HasValue) return st;
            }
            catch { }
            return ProfileQuestStatus(questId, null);
        }

        // Read the live status int (0–5) straight off a native quest object (QuestStatus/Status), or null.
        private static int? ReadQuestStatus(object quest)
        {
            object? statusObj = quest.GetType().GetProperty("QuestStatus", All)?.GetValue(quest)
                ?? quest.GetType().GetField("QuestStatus", All)?.GetValue(quest)
                ?? quest.GetType().GetProperty("Status", All)?.GetValue(quest);
            return statusObj != null ? Convert.ToInt32(statusObj) : (int?)null;
        }

        // Read (writeStatus == null) or write the EQuestStatus of a quest in Player.Profile.QuestsData — the
        // profile-level quest list (each entry has string Id + EQuestStatus Status). It exists in the hideout,
        // where there is no quest controller, so it is BOTH how the visibility gate reads state AND how we
        // locally reflect a server completion (write Success → gate hides + task UI updates without a reload).
        internal static int? ProfileQuestStatus(string questId, int? writeStatus)
        {
            try
            {
                EFT.Player player = EFT.GamePlayerOwner.MyPlayer;
                if (player == null) return null;
                object? profile = NativeBinder.GetProfile(player);
                if (profile == null) return null;
                if (!(profile.GetType().GetField("QuestsData", All)?.GetValue(profile) is IEnumerable quests)) return null;
                foreach (object item in quests)
                {
                    if (item == null) continue;
                    string? id = item.GetType().GetField("Id", All)?.GetValue(item) as string;
                    if (!string.Equals(id, questId, StringComparison.OrdinalIgnoreCase)) continue;
                    FieldInfo? statusField = item.GetType().GetField("Status", All);
                    if (statusField == null) return null;
                    if (writeStatus.HasValue)
                    {
                        statusField.SetValue(item, Enum.ToObject(statusField.FieldType, writeStatus.Value));
                        Plugin.Log.LogInfo("[NativeQuest] local profile: " + questId + " status -> " + writeStatus.Value);
                    }
                    object? st = statusField.GetValue(item);
                    return st != null ? Convert.ToInt32(st) : (int?)null;
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[NativeQuest] profileQuestStatus: " + (ex.InnerException?.Message ?? ex.Message)); }
            return null;
        }

        private static object? ResolveQuestController()
        {
            if (NativeBinder.ActiveQuestController != null) return NativeBinder.ActiveQuestController;
            EFT.Player player = EFT.GamePlayerOwner.MyPlayer;
            return player != null ? NativeBinder.GetQuestController(player) : null;
        }

        // The hideout has no quest controller on the PLAYER, but the menu/hideout keeps its OWN real one:
        // MainMenuControllerClass builds `new LocalQuestControllerClass(profile, InventoryController, ISession)`
        // and hands it to Singleton<HideoutClass>.Instance (stored in field HideoutClass.Gclass4005_0). That is
        // the EXACT controller EFT.UI.TasksScreen renders from, so completing the quest on IT (not a throwaway,
        // whose separate Quests list the board never reads) makes the task board refresh live + grants XP with
        // no restart. Reflection is cached — the hideout visibility gate calls this every frame.
        private static bool _hideoutReflectionTried;
        private static PropertyInfo? _hideoutInstanceProp;
        private static FieldInfo? _hideoutCtrlField;

        private static object? ResolveHideoutQuestController()
        {
            try
            {
                if (!_hideoutReflectionTried)
                {
                    _hideoutReflectionTried = true;
                    Type? hideoutType = AccessTools.TypeByName("HideoutClass");
                    Type? singletonOpen = AccessTools.TypeByName("Comfort.Common.Singleton`1");
                    if (hideoutType != null && singletonOpen != null)
                    {
                        Type singletonClosed = singletonOpen.MakeGenericType(hideoutType);
                        _hideoutInstanceProp = singletonClosed.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                        _hideoutCtrlField = hideoutType.GetField("Gclass4005_0", All);
                    }
                    Plugin.Log.LogInfo("[NativeQuest] hideout controller reflection: instanceProp=" + (_hideoutInstanceProp != null) + " ctrlField=" + (_hideoutCtrlField != null));
                }
                if (_hideoutInstanceProp == null || _hideoutCtrlField == null) return null;
                object? instance = _hideoutInstanceProp.GetValue(null);
                return instance != null ? _hideoutCtrlField.GetValue(instance) : null;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[NativeQuest] resolve hideout controller: " + (ex.InnerException?.Message ?? ex.Message));
                return null;
            }
        }

        private static bool TryNativeAcceptQuest(object ctrl, string questId)
        {
            try
            {
                object? quest = FindQuestInBook(ctrl, questId);
                if (quest == null) { Plugin.Log.LogWarning("[NativeQuest] " + questId + " not in QuestBook; cannot accept"); return false; }

                // A chain quest gated by an AvailableForStart `Quest` condition stays at Locked(0) until its
                // prereq is Success; the client may not have flipped it to AvailableForStart yet, so native
                // AcceptQuest (which expects AvailableForStart→Started) would no-op. Nudge it up first, silently.
                int? cur = ReadQuestStatus(quest);
                if (cur.HasValue && cur.Value < 1)
                    SetStatusOnController(ctrl, quest, questId, 1);

                MethodInfo? accept = ctrl.GetType().GetMethods(All)
                    .FirstOrDefault(m => m.Name == "AcceptQuest" && m.GetParameters().Length == 2);
                if (accept == null) { Plugin.Log.LogWarning("[NativeQuest] AcceptQuest(quest,bool) not found"); return false; }

                if (accept.Invoke(ctrl, new object[] { quest, true }) is Task task)
                    task.ContinueWith(t => Plugin.Log.LogInfo(t.IsFaulted
                        ? "[NativeQuest] AcceptQuest faulted: " + t.Exception?.InnerException?.Message
                        : "[NativeQuest] AcceptQuest completed: " + questId));
                Plugin.Log.LogInfo("[NativeQuest] native AcceptQuest triggered: " + questId);
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[NativeQuest] native accept failed: " + (ex.InnerException?.Message ?? ex.Message));
                return false;
            }
        }

        private static bool TryNativeCompleteQuest(object ctrl, string questId)
        {
            try
            {
                object? quest = FindQuestInBook(ctrl, questId);
                if (quest == null) { Plugin.Log.LogInfo("[NativeQuest] " + questId + " not in QuestBook; skip native complete"); return false; }

                MethodInfo? finish = ctrl.GetType().GetMethods(All).FirstOrDefault(m => m.Name == "FinishQuest" && m.GetParameters().Length == 2);
                if (finish == null) { Plugin.Log.LogWarning("[NativeQuest] FinishQuest not found"); return false; }

                // FinishQuest requires status AvailableForFinish(3) first — force it on THIS controller silently.
                SetStatusOnController(ctrl, quest, questId, 3, notify: false);
                if (finish.Invoke(ctrl, new object[] { quest, true }) is Task task)
                    task.ContinueWith(t => Plugin.Log.LogInfo(t.IsFaulted
                        ? "[NativeQuest] FinishQuest faulted: " + t.Exception?.InnerException?.Message
                        : "[NativeQuest] FinishQuest completed: " + questId));
                Plugin.Log.LogInfo("[NativeQuest] native FinishQuest triggered: " + questId);
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[NativeQuest] native complete failed: " + (ex.InnerException?.Message ?? ex.Message));
                return false;
            }
        }

        private static void TryNativeSetQuestStatus(string questId, int status, bool notify = true)
        {
            object? ctrl = ResolveQuestController();
            if (ctrl == null) { Plugin.Log.LogWarning("[NativeQuest] no quest controller (setStatus " + questId + ")"); return; }
            object? quest = FindQuestInBook(ctrl, questId);
            if (quest == null) { Plugin.Log.LogWarning("[NativeQuest] " + questId + " not found (setStatus)"); return; }
            SetStatusOnController(ctrl, quest, questId, status, notify);
        }

        // Transition a quest's status on a SPECIFIC controller (raid: the player's; hideout: one we built).
        // Tries the controller's TryExecuteTransition, then SetConditionalStatus, then the quest's own
        // TransitionStatus. notify=false is the silent pre-step before FinishQuest (advances conditions, no toast).
        private static void SetStatusOnController(object ctrl, object quest, string questId, int status, bool notify = true)
        {
            try
            {
                MethodInfo? tryExec = ctrl.GetType().GetMethods(All).FirstOrDefault(m => m.Name == "TryExecuteTransition" && m.GetParameters().Length == 2);
                if (tryExec != null)
                {
                    object st = Enum.ToObject(tryExec.GetParameters()[1].ParameterType, status);
                    object result = tryExec.Invoke(ctrl, new object[] { quest, st });
                    Plugin.Log.LogInfo("[NativeQuest] TryExecuteTransition " + questId + " -> " + status + ", result=" + result);
                    if (result is bool b && b) return;
                }

                MethodInfo? setCond = ctrl.GetType().GetMethods(All).FirstOrDefault(m => m.Name == "SetConditionalStatus" && m.GetParameters().Length == 2);
                if (setCond != null)
                {
                    object st = Enum.ToObject(setCond.GetParameters()[1].ParameterType, status);
                    setCond.Invoke(ctrl, new object[] { quest, st });
                    Plugin.Log.LogInfo("[NativeQuest] SetConditionalStatus " + questId + " -> " + status);
                    // SetConditionalStatus already raises the status-changed notification internally; firing
                    // OnConditionalStatusChangedEvent again double-showed the "quest ready" toast. The silent
                    // complete pre-step (notify=false) still advances conditions so FinishQuest sees them done.
                    if (!notify) TryAdvanceQuestConditions(ctrl, quest);
                    return;
                }

                MethodInfo? transition = quest.GetType().GetMethods(All).FirstOrDefault(m => m.Name == "TransitionStatus" && m.GetParameters().Length == 2);
                if (transition != null)
                {
                    object st = Enum.ToObject(transition.GetParameters()[0].ParameterType, status);
                    transition.Invoke(quest, new object[] { st, true });
                    Plugin.Log.LogInfo("[NativeQuest] TransitionStatus " + questId + " -> " + status);
                    // TransitionStatus(fromServer:true) self-notifies; don't fire the event again (double toast).
                    if (!notify) TryAdvanceQuestConditions(ctrl, quest);
                }
                else
                {
                    Plugin.Log.LogWarning("[NativeQuest] no transition method for " + questId);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[NativeQuest] setStatus: " + (ex.InnerException?.Message ?? ex.Message));
            }
        }

        private static object? FindQuestInBook(object ctrl, string questId)
        {
            if (!(ctrl.GetType().GetProperty("Quests", All)?.GetValue(ctrl) is IEnumerable quests)) return null;
            foreach (object item in quests)
            {
                if (item == null) continue;
                object? tmpl = item.GetType().GetProperty("Template", All)?.GetValue(item);
                string? id = tmpl?.GetType().GetProperty("Id", All)?.GetValue(tmpl) as string;
                if (string.Equals(id, questId, StringComparison.OrdinalIgnoreCase)) return item;
            }
            return null;
        }

        private static void ForEachNativeQuest(Action<string, int> onQuest)
        {
            object? ctrl = ResolveQuestController();
            if (ctrl == null || !(ctrl.GetType().GetProperty("Quests", All)?.GetValue(ctrl) is IEnumerable quests)) return;
            foreach (object item in quests)
            {
                if (item == null) continue;
                object? tmpl = item.GetType().GetProperty("Template", All)?.GetValue(item);
                string? id = tmpl?.GetType().GetProperty("Id", All)?.GetValue(tmpl) as string;
                if (string.IsNullOrEmpty(id)) continue;
                object? statusObj = item.GetType().GetProperty("QuestStatus", All)?.GetValue(item)
                    ?? item.GetType().GetField("QuestStatus", All)?.GetValue(item)
                    ?? item.GetType().GetProperty("Status", All)?.GetValue(item);
                if (statusObj != null) onQuest(id!, Convert.ToInt32(statusObj));
            }
        }

        private static Dictionary<string, int> ReadNativeStatuses()
        {
            Dictionary<string, int> map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            ForEachNativeQuest((id, status) => map[id] = status);
            return map;
        }

        // After a status change, EFT does not re-evaluate OTHER quests' ConditionQuest gates. Quests still in
        // Started whose finish-conditions are now all met (and at least one references the changed quest) get
        // pushed to AvailableForFinish + a notification — see DEV_NOTES #19.
        private static void ReevaluateReadyQuests(string changedQuestId)
        {
            try
            {
                object? ctrl = ResolveQuestController();
                if (ctrl == null || !(ctrl.GetType().GetProperty("Quests", All)?.GetValue(ctrl) is IEnumerable quests)) return;
                Dictionary<string, int> statusMap = ReadNativeStatuses();
                MethodInfo? tryExec = ctrl.GetType().GetMethods(All).FirstOrDefault(m => m.Name == "TryExecuteTransition" && m.GetParameters().Length == 2);
                MethodInfo? setCond = ctrl.GetType().GetMethods(All).FirstOrDefault(m => m.Name == "SetConditionalStatus" && m.GetParameters().Length == 2);
                Type? statusType = (tryExec ?? setCond)?.GetParameters()[1].ParameterType;
                if (statusType == null) return;
                object availForFinish = Enum.ToObject(statusType, 3);

                foreach (object q in quests)
                {
                    if (q == null) continue;
                    object? statusObj = q.GetType().GetProperty("QuestStatus", All)?.GetValue(q);
                    if (statusObj == null || Convert.ToInt32(statusObj) != 2) continue; // only Started
                    object? tmpl = q.GetType().GetProperty("Template", All)?.GetValue(q);
                    string? qid = tmpl?.GetType().GetProperty("Id", All)?.GetValue(tmpl) as string;
                    if (string.IsNullOrEmpty(qid)) continue;
                    if (!(q.GetType().GetProperty("NecessaryConditions", All)?.GetValue(q) is IEnumerable conds)) continue;
                    if (!AllFinishConditionsMet(q, conds, statusMap, changedQuestId, out bool referencesChanged) || !referencesChanged) continue;

                    bool moved = tryExec != null && tryExec.Invoke(ctrl, new object[] { q, availForFinish }) is bool b && b;
                    if (!moved && setCond != null) { setCond.Invoke(ctrl, new object[] { q, availForFinish }); moved = true; }
                    if (moved)
                    {
                        TryFireQuestNotification(ctrl, q);
                        Plugin.Log.LogInfo("[NativeQuest] dependent quest " + qid + " -> AvailableForFinish (by " + changedQuestId + ")");
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[NativeQuest] ReevaluateReadyQuests: " + (ex.InnerException?.Message ?? ex.Message));
            }
        }

        private static bool AllFinishConditionsMet(object quest, IEnumerable conds, Dictionary<string, int> statusMap, string changedQuestId, out bool referencesChanged)
        {
            referencesChanged = false;
            MethodInfo? isDone = quest.GetType().GetMethod("IsConditionDone", All);
            int count = 0;
            foreach (object cond in conds)
            {
                if (cond == null) continue;
                count++;
                if (cond.GetType().Name == "ConditionQuest")
                {
                    string? target = cond.GetType().GetField("target", All)?.GetValue(cond) as string;
                    if (string.IsNullOrEmpty(target) || !(cond.GetType().GetField("statuses", All)?.GetValue(cond) is IEnumerable statuses) || !statusMap.TryGetValue(target!, out int cur))
                        return false;
                    bool match = false;
                    foreach (object s in statuses) if (Convert.ToInt32(s) == cur) { match = true; break; }
                    if (!match) return false;
                    if (string.Equals(target, changedQuestId, StringComparison.OrdinalIgnoreCase)) referencesChanged = true;
                }
                else if (isDone != null)
                {
                    if (!(isDone.Invoke(quest, new object[] { cond }) is bool b && b)) return false;
                }
                else return false;
            }
            return count > 0;
        }

        private static void TryFireQuestNotification(object ctrl, object quest)
        {
            try
            {
                // Only the status-changed event — NOT TryAdvanceQuestConditions too. The status is already set
                // (SetConditionalStatus/TryExecuteTransition); firing both produced a DOUBLE "quest ready"
                // toast. OnConditionalStatusChangedEvent is the canonical "→ AvailableForFinish" notification.
                MethodInfo? evt = ctrl.GetType().GetMethods(All).FirstOrDefault(m => m.Name == "OnConditionalStatusChangedEvent" && m.GetParameters().Length == 2);
                if (evt != null) evt.Invoke(ctrl, new object[] { quest, true });
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[NativeQuest] fireNotification: " + (ex.InnerException?.Message ?? ex.Message));
            }
        }

        private static void TryAdvanceQuestConditions(object ctrl, object quest)
        {
            try
            {
                if (!(quest.GetType().GetProperty("NecessaryConditions", All)?.GetValue(quest) is IEnumerable conds)) return;
                MethodInfo? tryExec = ctrl.GetType().GetMethods(All).FirstOrDefault(m => m.Name == "TryExecuteTransition" && m.GetParameters().Length == 2);
                if (tryExec == null) return;
                object availForFinish = Enum.ToObject(tryExec.GetParameters()[1].ParameterType, 3);
                MethodInfo? onChanged = ctrl.GetType().GetMethods(All).FirstOrDefault(m => m.Name == "OnConditionValueChanged" && m.GetParameters().Length == 4 && !m.IsGenericMethodDefinition);
                if (onChanged == null) return;
                foreach (object cond in conds)
                    if (cond != null) onChanged.Invoke(ctrl, new object[] { quest, availForFinish, cond, true });
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[NativeQuest] advanceConditions: " + (ex.InnerException?.Message ?? ex.Message));
            }
        }

        private static void TryPlayQuestCompletedSound()
        {
            try
            {
                Type? guiSounds = AccessTools.TypeByName("EFT.UI.GUISounds") ?? AccessTools.TypeByName("GUISounds");
                if (guiSounds == null) return;
                UnityEngine.Object inst = UnityEngine.Object.FindObjectOfType(guiSounds);
                if (inst == null) return;
                MethodInfo? play = guiSounds.GetMethods(All).FirstOrDefault(m => m.Name == "PlayUISound" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.IsEnum);
                if (play == null) return;
                Type soundType = play.GetParameters()[0].ParameterType;
                foreach (string name in new[] { "QuestCompleted", "QuestFinished", "QuestComplete", "QuestSubTrackComplete", "TradeOperationComplete" })
                    if (Enum.IsDefined(soundType, name)) { play.Invoke(inst, new object[] { Enum.Parse(soundType, name) }); return; }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[NativeQuest] questSound: " + (ex.InnerException?.Message ?? ex.Message));
            }
        }
    }
}
