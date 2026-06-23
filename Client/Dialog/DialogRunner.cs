using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VisitAPI.Native;

namespace VisitAPI
{
    internal static class DialogRunner
    {
        private static readonly List<UnityEngine.Object> _spawnedRows = new List<UnityEngine.Object>();

        // fromMenu = opened by the out-of-raid 对话 button. The menu entry only ever enters via the root family
        // (root / root_high / …), never the first-meeting node — that's in-raid story. Raid/hideout keep the
        // first-visit gate. Either way the repeat/root entry is chosen by the `when:` conditions (level/standing).
        internal static IEnumerator Begin(DialogTree tree, bool fromMenu = false)
        {
            object window = null;
            for (int i = 0; i < 180 && window == null; i++)
            {
                object screen = DialogUiBinder.FindActiveScreen();
                if (screen != null) window = DialogUiBinder.GetWindow(screen);
                if (window == null) yield return null;
            }

            if (window == null)
            {
                Plugin.Log.LogWarning("[DialogRunner] dialog window not found after waiting");
                yield break;
            }

            string traderId = NativeBinder.ActiveTraderId;
            string profileId = NativeBinder.ActiveProfileId;
            bool firstVisit = !fromMenu && !string.IsNullOrEmpty(tree.FirstVisitNode) && DialogStateStore.IsFirstVisit(traderId, profileId);
            string start;
            if (firstVisit)
            {
                DialogStateStore.MarkVisited(traderId, profileId);
                start = tree.FirstVisitNode!;
            }
            else
            {
                start = ResolveStartNode(tree);
            }
            Plugin.Log.LogInfo("[DialogRunner] window found; rendering node '" + start + "' (firstVisit=" + firstVisit + ", fromMenu=" + fromMenu + ")");
            RenderNode(window, tree, start);
        }

        // Repeat/root entry node, chosen by the `when:` conditions: the first condition whose level AND standing
        // range contains the player wins (file order, so list strongest first). No match → `start:` (root). The
        // active profile (raid: player; menu: trade screen) supplies level + this trader's standing.
        private static string ResolveStartNode(DialogTree tree)
        {
            string fallback = tree.Nodes.ContainsKey(tree.StartNode) ? tree.StartNode : (tree.FirstVisitNode ?? tree.StartNode);
            if (tree.NodeConditions == null || tree.NodeConditions.Count == 0) return fallback;

            object? profile = NativeBinder.ActiveProfile;
            int level = NativeBinder.GetPlayerLevel(profile);
            double standing = NativeBinder.GetTraderStanding(profile, NativeBinder.ActiveTraderId);
            foreach (NodeCondition c in tree.NodeConditions)
            {
                if (level < c.MinLevel || level > c.MaxLevel) continue;
                if (standing < c.MinStanding || standing > c.MaxStanding) continue;
                if (!tree.Nodes.ContainsKey(c.Node)) continue;
                Plugin.Log.LogInfo("[DialogRunner] when -> '" + c.Node + "' (level=" + level + ", standing=" + standing.ToString("0.###") + ")");
                return c.Node;
            }
            return fallback;
        }

        // Open straight onto a specific node (e.g. a hideout trigger resuming a questline mid-story), bypassing
        // the first-visit/root selection that Begin does.
        internal static IEnumerator BeginAt(DialogTree tree, string startNode)
        {
            object window = null;
            for (int i = 0; i < 180 && window == null; i++)
            {
                object screen = DialogUiBinder.FindActiveScreen();
                if (screen != null) window = DialogUiBinder.GetWindow(screen);
                if (window == null) yield return null;
            }
            if (window == null)
            {
                Plugin.Log.LogWarning("[DialogRunner] dialog window not found after waiting");
                yield break;
            }
            string start = !string.IsNullOrEmpty(startNode) && tree.Nodes.ContainsKey(startNode)
                ? startNode
                : (tree.Nodes.ContainsKey(tree.StartNode) ? tree.StartNode : (tree.FirstVisitNode ?? tree.StartNode));
            Plugin.Log.LogInfo("[DialogRunner] window found; rendering forced node '" + start + "'");
            RenderNode(window, tree, start);
        }

        internal static void CloseDialog()
        {
            ClearRows();
            DialogUiBinder.CloseActiveScreen();
        }

        // After CloseDialog reveals the trade screen, switch it to the requested tab (Trade/Tasks/Services). Waits a
        // couple frames so the screen is active again before method_3 re-shows the tab content.
        private static IEnumerator SwitchTabAfterClose(string mode)
        {
            yield return null;
            yield return null;
            if (!NativeBinder.SwitchTradeTab(mode))
                Plugin.Log.LogWarning("[DialogRunner] tab switch failed after close: " + mode);
        }

        private static void RenderNode(object window, DialogTree tree, string nodeName)
        {
            if (string.IsNullOrEmpty(nodeName) || !tree.Nodes.TryGetValue(nodeName, out DialogNode node))
            {
                Plugin.Log.LogWarning("[DialogRunner] node not found: '" + nodeName + "'");
                return;
            }
            DialogUiBinder.SetBackground(node.Background);
            // Narration (`>` lines) plays in the native subtitle box — its OWN dialog box — one line at a time
            // (click to advance) with the main dialog window hidden; THEN the NPC line + options render. A pure
            // narration node (no NPC text / options) jumps to its `-> ` target (纯旁白跳转节点) or closes at the end.
            if (node.Narration != null && node.Narration.Count > 0)
                PlayNarration(window, tree, node, nodeName);
            else
                RenderBody(window, tree, node, nodeName);
        }

        private static void PlayNarration(object window, DialogTree tree, DialogNode node, string nodeName)
        {
            object? screen = DialogUiBinder.FindActiveScreen();
            Queue<KeyValuePair<string, string?>> lines = new Queue<KeyValuePair<string, string?>>();
            List<string> narration = node.Narration!;
            List<string?>? backgrounds = node.NarrationBackgrounds;
            for (int i = 0; i < narration.Count; i++)
            {
                if (string.IsNullOrEmpty(narration[i])) continue;
                string? lineBg = (backgrounds != null && i < backgrounds.Count) ? backgrounds[i] : null;
                lines.Enqueue(new KeyValuePair<string, string?>(Substitute(narration[i]), lineBg));
            }
            if (lines.Count == 0) { RenderBody(window, tree, node, nodeName); return; }
            DialogUiBinder.BeginNarration(screen);
            AdvanceNarration(window, tree, node, nodeName, screen, lines);
        }

        // Show the next narration line in the subtitle box; the click overlay advances to the line after it, or —
        // on the last line — restores the dialog window and renders the body (or jumps/closes for a pure-narration node).
        private static void AdvanceNarration(object window, DialogTree tree, DialogNode node, string nodeName, object? screen, Queue<KeyValuePair<string, string?>> lines)
        {
            KeyValuePair<string, string?> current = lines.Dequeue();
            if (!string.IsNullOrEmpty(current.Value))
                DialogUiBinder.SetBackground(current.Value);
            DialogUiBinder.SetSubtitleText(screen, current.Key);
            if (lines.Count > 0)
            {
                NarrationOverlay.SetClickHandler(() => AdvanceNarration(window, tree, node, nodeName, screen, lines));
                return;
            }
            NarrationOverlay.SetClickHandler(() =>
            {
                NarrationOverlay.SetClickHandler(null);
                DialogUiBinder.EndNarration(screen);
                if (node.Options.Count > 0 || !string.IsNullOrEmpty(node.NpcText))
                    RenderBody(window, tree, node, nodeName);
                else if (!string.IsNullOrEmpty(node.JumpTo) && node.JumpTo != "@close" && node.JumpTo != "@leave")
                    RenderNode(window, tree, node.JumpTo == "@start" ? tree.StartNode : node.JumpTo!);
                else
                    CloseDialog();
            });
        }

        private static void RenderBody(object window, DialogTree tree, DialogNode node, string nodeName)
        {
            DialogUiBinder.ShowWindow(window);
            DialogUiBinder.SetTraderText(window, Substitute(node.NpcText));
            ClearRows();

            string traderId = NativeBinder.ActiveTraderId;
            string profileId = NativeBinder.ActiveProfileId;

            // Choose which options to show: drop one-time options already chosen, and quest-status-gated
            // options whose gate doesn't match the current quest status. If that would leave NOTHING, show
            // every option instead — a node with zero options soft-locks the player in raid (ESC is blocked).
            List<int> visible = new List<int>();
            for (int i = 0; i < node.Options.Count; i++)
            {
                DialogOption o = node.Options[i];
                if (o.Once && DialogStateStore.IsSeen(traderId, profileId, nodeName, i)) continue;
                if (!OptionVisible(o)) continue;
                visible.Add(i);
            }
            if (visible.Count == 0 && node.Options.Count > 0)
            {
                Plugin.Log.LogWarning("[DialogRunner] node '" + nodeName + "': every option filtered out; showing all to avoid a dead-end");
                for (int i = 0; i < node.Options.Count; i++) visible.Add(i);
            }

            int shown = 0;
            foreach (int i in visible)
            {
                DialogOption opt = node.Options[i];
                UnityEngine.Object rowObj = DialogUiBinder.InstantiateRow(window);
                if (rowObj == null) continue;
                DialogUiBinder.SetRowText(rowObj, Substitute(opt.Text));
                DialogUiBinder.ResetRowHighlight(rowObj);

                GameObject go = ((Component)rowObj).gameObject;
                VisitOptionRow marker = go.AddComponent<VisitOptionRow>();
                DialogOption captured = opt;
                int idx = i;
                marker.OnClick = delegate
                {
                    if (captured.Once) DialogStateStore.MarkSeen(traderId, profileId, nodeName, idx);
                    OnOptionClicked(window, tree, captured);
                };
                go.SetActive(true);
                _spawnedRows.Add(rowObj);
                shown++;
            }

            // Option-less node (no narration jump either): a single continue/end affordance.
            if (node.Options.Count == 0 && SpawnContinueRow(window, tree, node)) shown++;

            DialogUiBinder.SetOptionsInteractable(window);
            Plugin.Log.LogInfo("[DialogRunner] rendered node '" + nodeName + "' with " + shown + " option(s)");
        }

        // {playerName} / {player} substitution for narration, NPC text and option labels.
        private static string _playerName = "";
        private static string Substitute(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (_playerName.Length == 0) ResolvePlayerName();
            return _playerName.Length == 0 ? text! : text!.Replace("{playerName}", _playerName).Replace("{player}", _playerName);
        }

        private static void ResolvePlayerName()
        {
            try
            {
                // Raid/hideout: the local player. Menu (no MyPlayer): the profile stashed by the open path.
                EFT.Player p = EFT.GamePlayerOwner.MyPlayer;
                object? profile = (p != null ? NativeBinder.GetProfile(p) : null) ?? NativeBinder.ActiveProfile;
                if (profile == null) return;
                object? nick = profile.GetType().GetProperty("Nickname")?.GetValue(profile)
                    ?? (profile.GetType().GetProperty("Info")?.GetValue(profile) is object info
                        ? info.GetType().GetProperty("Nickname")?.GetValue(info) : null);
                _playerName = nick as string ?? "";
            }
            catch { }
        }

        // The single "continue/end" row for an option-less node. Jumps to node.JumpTo (`-> X`), or closes.
        private static bool SpawnContinueRow(object window, DialogTree tree, DialogNode node)
        {
            string? jump = node.JumpTo;
            bool closes = string.IsNullOrEmpty(jump) || jump == "@close" || jump == "@leave";
            UnityEngine.Object rowObj = DialogUiBinder.InstantiateRow(window);
            if (rowObj == null) return false;
            DialogUiBinder.SetRowText(rowObj, closes ? Loc.RowEnd : Loc.RowContinue);
            DialogUiBinder.ResetRowHighlight(rowObj);
            GameObject go = ((Component)rowObj).gameObject;
            VisitOptionRow marker = go.AddComponent<VisitOptionRow>();
            string? target = closes ? null : (jump == "@start" ? tree.StartNode : jump);
            marker.OnClick = delegate
            {
                if (string.IsNullOrEmpty(target)) CloseDialog();
                else RenderNode(window, tree, target!);
            };
            go.SetActive(true);
            _spawnedRows.Add(rowObj);
            return true;
        }

        // Quest-status gate: an option with ShowWhenStatus only shows while its quest is in one of those
        // statuses; HideWhenStatus hides it while in those. No gate (or no quest id) → always visible.
        private static bool OptionVisible(DialogOption opt)
        {
            bool hasShow = opt.ShowWhenStatus != null && opt.ShowWhenStatus.Count > 0;
            bool hasHide = opt.HideWhenStatus != null && opt.HideWhenStatus.Count > 0;
            if (!hasShow && !hasHide) return true;
            if (string.IsNullOrEmpty(opt.QuestId)) return true;
            int? cur = QuestStatusCache.StatusOf(opt.QuestId!);
            if (hasShow && !QuestStatusCache.InAny(cur, opt.ShowWhenStatus!)) return false;
            if (hasHide && QuestStatusCache.InAny(cur, opt.HideWhenStatus!)) return false;
            return true;
        }

        private static void OnOptionClicked(object window, DialogTree tree, DialogOption opt)
        {
            // Quest-action modifiers fire alongside whatever else the option does.
            if (!string.IsNullOrEmpty(opt.AcceptQuestId))
                NativeQuestController.AcceptQuest(opt.AcceptQuestId!);
            if (!string.IsNullOrEmpty(opt.SetStatusQuestId))
                NativeQuestController.SetQuestStatus(opt.SetStatusQuestId!, opt.SetStatusValue ?? 3);
            if (opt.StandingDelta.HasValue && opt.StandingDelta.Value != 0.0)
                StandingService.Apply(string.IsNullOrEmpty(opt.StandingTarget) ? NativeBinder.ActiveTraderId : opt.StandingTarget!, opt.StandingDelta.Value);

            switch (opt.Action)
            {
                case "acceptQuest":
                    NativeQuestController.AcceptQuest(opt.QuestId ?? "");
                    break;
                case "completeQuest":
                    // Refused (returns false) when the quest still owes a hand-over — don't advance/close as if it
                    // finished; stay on the node so the player can fetch the rest and come back.
                    if (!NativeQuestController.CompleteQuest(opt.QuestId ?? ""))
                    {
                        Plugin.Log.LogInfo("[DialogRunner] complete refused (conditions unmet); staying on node");
                        return;
                    }
                    break;
                case "handoverItems":
                {
                    // Native item-selection window(s) are async (player picks exact amounts; the UI splits stacks).
                    // Continue the dialog ONLY after every handover window is done; stay on this node if cancelled.
                    object capturedWindow = window;
                    DialogTree capturedTree = tree;
                    string? next = opt.Next;
                    NativeQuestController.HandoverItemViaWindow(opt.QuestId ?? "", opt.HandoverLabel, ok =>
                    {
                        if (!ok) return;
                        if (!string.IsNullOrEmpty(next))
                            RenderNode(capturedWindow, capturedTree, next == "@start" ? capturedTree.StartNode : next!);
                        else
                            CloseDialog();
                    });
                    return;
                }
                case "openTrade":
                case "openTasks":
                case "openServices":
                {
                    // Only valid out of raid (the 对话 button opens us on top of TraderScreensGroup); in raid there's
                    // no trade screen, so we stay on the node. Close the dialog FIRST, then switch the tab once the
                    // trade screen is the active screen again — switching while it's still covered by the dialog can
                    // run method_3 (which re-shows the tab content) on a deactivated screen.
                    if (NativeBinder.ActiveTradeScreen == null)
                    {
                        Plugin.Log.LogInfo("[DialogRunner] '" + opt.Action + "' ignored — no out-of-raid trade screen behind the dialog");
                        return;
                    }
                    string mode = opt.Action == "openTrade" ? "Trade" : opt.Action == "openTasks" ? "Tasks" : "Services";
                    CloseDialog();
                    Plugin.Instance.StartCoroutine(SwitchTabAfterClose(mode));
                    return;
                }
            }

            if (!string.IsNullOrEmpty(opt.Next))
            {
                RenderNode(window, tree, opt.Next == "@start" ? tree.StartNode : opt.Next!);
                return;
            }
            CloseDialog();
        }

        private static void ClearRows()
        {
            foreach (UnityEngine.Object row in _spawnedRows)
            {
                if (row != null) UnityEngine.Object.Destroy(((Component)row).gameObject);
            }
            _spawnedRows.Clear();
        }
    }
}
