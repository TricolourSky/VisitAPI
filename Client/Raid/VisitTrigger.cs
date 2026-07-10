using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using UnityEngine;
using VisitAPI.Native;

namespace VisitAPI
{
    // The one in-world interaction trigger, raid AND hideout (HideoutPlayerOwner : GamePlayerOwner, so a single
    // typed FindObjectOfType covers both). Everything runs through the public native interaction state
    // (GamePlayerOwner.AvailableInteractionState) — no reflection. Three shapes, all the same component:
    //   - raid point:     fixed position + look-angle gate, REPLACES the interaction state
    //   - hideout area:   fixed position, MERGES our action into the native area menu (one-frame handshake)
    //   - free-standing:  fixed position + look-angle gate, replaces (open floor, no native menu to merge into)
    internal sealed class VisitTrigger : MonoBehaviour
    {
        internal string TraderId = "";
        internal string PromptText = "";
        internal Vector3 FixedPosition;
        internal float MaxDistance = 3f;
        internal float HitRadius = 1.2f;
        internal bool MergeIntoNativeMenu;
        internal bool RequireLook;
        internal string? Node;
        internal string? QuestId;
        internal List<string>? ShowWhenStatus;

        private GamePlayerOwner? _owner;
        private Camera? _cam;
        private bool _shown;
        private int _nativeFirstSeenFrame = -1;
        private float _ownerFindAt;
        private float _cooldownUntil;

        private void Update()
        {
            if (!EnsureOwner()) return;
            if (Time.unscaledTime >= _cooldownUntil && ShouldShow()) Show();
            else Hide();
        }

        private bool EnsureOwner()
        {
            if (_owner != null) return true;
            if (Time.unscaledTime < _ownerFindAt) return false;
            _ownerFindAt = Time.unscaledTime + 1f;
            _owner = FindObjectOfType<GamePlayerOwner>();
            if (_owner != null)
                Plugin.Log.LogInfo("[VisitTrigger] bound " + _owner.GetType().Name + " (" + TraderId + ")");
            return _owner != null;
        }

        private bool ShouldShow()
        {
            Vector3 target = FixedPosition;
            if (_cam == null || !_cam.isActiveAndEnabled)
            {
                _cam = Camera.main;
                if (_cam == null) return false;
            }
            Transform ct = _cam.transform;
            float dist = Vector3.Distance(ct.position, target);
            if (dist > MaxDistance) return false;
            if (RequireLook)
            {
                float angle = Vector3.Angle(ct.forward, (target - ct.position).normalized);
                float maxAngle = Mathf.Clamp(Mathf.Atan2(HitRadius, Mathf.Max(dist, 0.5f)) * Mathf.Rad2Deg, 8f, 40f);
                if (angle > maxAngle) return false;
            }
            return QuestGatePasses();
        }

        // A live NPC model: first a spawned bot whose nickname contains the name, then an exact-named scene
        // GameObject (a model placed by the trader's own mod). Used by the dialog actor animations (`actor:`).
        internal static Transform? FindNpcTransform(string name)
        {
            try
            {
                if (Singleton<GameWorld>.Instantiated)
                {
                    GameWorld gw = Singleton<GameWorld>.Instance;
                    foreach (Player p in gw.AllAlivePlayersList)
                    {
                        if (p == null || p == gw.MainPlayer) continue;
                        string nick = p.Profile?.Nickname ?? "";
                        if (nick.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) return ((Component)p).transform;
                    }
                }
            }
            catch { }
            GameObject go = GameObject.Find(name);
            return go != null ? go.transform : null;
        }

        // Strict quest gate: with an `if quest=status` the trigger shows ONLY while the quest is in one of those
        // statuses; an unreadable status (never-started quest → null) counts as no match → hidden. Strictness is
        // what keeps two triggers parked at the same spot mutually exclusive (DEV_NOTES #22).
        private bool QuestGatePasses()
        {
            if (string.IsNullOrEmpty(QuestId) || ShowWhenStatus == null || ShowWhenStatus.Count == 0) return true;
            return QuestStatusCache.InAny(QuestStatusCache.StatusOf(QuestId!), ShowWhenStatus);
        }

        private void Show()
        {
            var state = _owner!.AvailableInteractionState;
            ActionsReturnClass? current = state.Value;
            if (current?.Actions != null)
            {
                foreach (ActionsTypesClass a in current.Actions)
                    if (a != null && a.Name == PromptText) { _shown = true; return; }
            }

            if (MergeIntoNativeMenu)
            {
                // No native area menu up → nothing to merge into. One-frame handshake so we don't race EFT's
                // own set the frame the menu first appears.
                if (current?.Actions == null)
                {
                    _shown = false;
                    _nativeFirstSeenFrame = -1;
                    return;
                }
                if (_nativeFirstSeenFrame < 0) { _nativeFirstSeenFrame = Time.frameCount; return; }
                if (Time.frameCount == _nativeFirstSeenFrame) return;
                _nativeFirstSeenFrame = -1;
            }

            ActionsReturnClass menu = new ActionsReturnClass();
            if (MergeIntoNativeMenu && current?.Actions != null)
                foreach (ActionsTypesClass a in current.Actions) menu.Actions.Add(a);
            menu.Actions.Add(new ActionsTypesClass { Name = PromptText, Action = FireVisit });
            menu.InitSelected();
            state.Value = menu;
            _shown = true;
        }

        private void Hide()
        {
            _nativeFirstSeenFrame = -1;
            if (!_shown) return;
            _shown = false;
            try
            {
                var state = _owner!.AvailableInteractionState;
                ActionsReturnClass? current = state.Value;
                if (current?.Actions == null) return;
                // Remove ONLY our action; leave whatever the game put there intact.
                ActionsReturnClass cleaned = new ActionsReturnClass();
                bool hadOurs = false;
                foreach (ActionsTypesClass a in current.Actions)
                {
                    if (a != null && a.Name == PromptText) { hadOurs = true; continue; }
                    cleaned.Actions.Add(a);
                }
                if (!hadOurs) return;
                if (cleaned.Actions.Count > 0)
                {
                    cleaned.InitSelected();
                    state.Value = cleaned;
                }
                else
                {
                    state.Value = null;
                }
            }
            catch { }
        }

        private void FireVisit()
        {
            Hide();
            _cooldownUntil = Time.unscaledTime + 1.5f;
            DialogTree? tree = DialogTreeLoader.Load(TraderId);
            if (tree == null)
            {
                Plugin.Log.LogWarning("[VisitTrigger] no .dlg for " + TraderId);
                return;
            }
            Plugin.Log.LogInfo("[VisitTrigger] interact -> opening dialog " + TraderId + " at node '" + (Node ?? "(default)") + "'");
            if (DialogOpener.TryOpen(TraderId, out string error))
                StartCoroutine(DialogRunner.Begin(tree, fromMenu: false, forcedNode: Node));
            else
                Plugin.Log.LogWarning("[VisitTrigger] open failed: " + error);
        }

        private void OnDestroy() => Hide();
    }
}
