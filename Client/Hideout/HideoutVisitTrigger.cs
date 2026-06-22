using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using VisitAPI.Native;

namespace VisitAPI
{
    // Hideout counterpart of RaidVisitTrigger. The hideout's AvailableInteractionState is SHARED, so we MERGE
    // our "visit" action INTO the native area menu (e.g. the Intelligence Center menu) instead of replacing it:
    // append our action to the native list, and on leave remove only ours. A one-frame handshake avoids racing
    // EFT's own set the frame the menu appears. (Native-EFT-supported; matches old 0.2.4 behaviour.)
    internal sealed class HideoutVisitTrigger : MonoBehaviour
    {
        internal string TraderId = "";
        internal string PromptText = "";
        internal Vector3 TriggerPosition;
        internal float MaxDistance = 3f;
        internal string? Node;
        internal string? QuestId;
        internal List<string>? ShowWhenStatus;
        internal bool FreeStanding;
        internal float HitRadius = 1.2f;

        private static Type? _hpoType;

        private UnityEngine.Object? _owner;
        private FieldInfo? _stateField;
        private MethodInfo? _setValue;
        private MethodInfo? _getValue;
        private Camera? _cam;
        private bool _merged;
        private int _nativeFirstSeenFrame = -1;
        private float _findAt;
        private float _cooldownUntil;

        private void Update()
        {
            if (!EnsureBinding()) return;
            if (Time.unscaledTime >= _cooldownUntil && ShouldShow()) Show();
            else Hide();
        }

        private bool EnsureBinding()
        {
            if ((UnityEngine.Object)(object)_owner != (UnityEngine.Object)null) return _setValue != null;
            if (Time.unscaledTime < _findAt) return false;
            _findAt = Time.unscaledTime + 1f;
            if (_hpoType == null)
                _hpoType = AccessTools.TypeByName("EFT.HideoutPlayerOwner") ?? AccessTools.TypeByName("EFT.Hideout.HideoutPlayerOwner");
            if (_hpoType == null) return false;
            _owner = UnityEngine.Object.FindObjectOfType(_hpoType);
            if ((UnityEngine.Object)(object)_owner == (UnityEngine.Object)null) return false;
            _stateField = _owner.GetType().GetField("AvailableInteractionState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object? state = _stateField?.GetValue(_owner);
            _setValue = state?.GetType().GetMethod("set_Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _getValue = state?.GetType().GetMethod("get_Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            bool ok = _setValue != null && _getValue != null;
            Plugin.Log.LogInfo("[HideoutTrigger] HideoutPlayerOwner bind " + (ok ? "PASS" : "FAIL") + " (" + TraderId + ")");
            return ok;
        }

        private bool ShouldShow()
        {
            if ((UnityEngine.Object)(object)_cam == (UnityEngine.Object)null || !_cam.isActiveAndEnabled)
            {
                _cam = Camera.main;
                if ((UnityEngine.Object)(object)_cam == (UnityEngine.Object)null) return false;
            }
            Transform ct = _cam.transform;
            float dist = Vector3.Distance(ct.position, TriggerPosition);
            if (dist > MaxDistance) return false;
            // Free-standing (a spot with no native interaction menu, e.g. the open floor by the gym): require the
            // player to LOOK at the point — otherwise we'd blanket-replace the interaction state just for being near.
            if (FreeStanding)
            {
                Vector3 toTarget = TriggerPosition - ct.position;
                float angle = Vector3.Angle(ct.forward, toTarget.normalized);
                float maxAngle = Mathf.Clamp(Mathf.Atan2(HitRadius, Mathf.Max(dist, 0.5f)) * Mathf.Rad2Deg, 8f, 40f);
                if (angle > maxAngle) return false;
            }
            return QuestGatePasses();
        }

        // Strict quest gate: the interaction appears ONLY while its quest is in one of ShowWhenStatus. An
        // unreadable status (a quest never started has no profile entry → null) counts as "no match" → hidden.
        // Status reads reliably in the hideout now via Player.Profile.QuestsData (NativeQuestController.
        // GetQuestStatus), so leniency is no longer needed — and it's what keeps two triggers parked at the SAME
        // spot mutually exclusive: e.g. node C shows only while quest1=Started, node D only while quest2=
        // AvailableForFinish. Without strictness, a still-Locked quest2 reads null and trigger D would show on
        // the first visit, racing trigger C (same label/position).
        private bool QuestGatePasses()
        {
            if (string.IsNullOrEmpty(QuestId) || ShowWhenStatus == null || ShowWhenStatus.Count == 0) return true;
            return QuestStatusCache.InAny(QuestStatusCache.StatusOf(QuestId!), ShowWhenStatus);
        }

        private void Show()
        {
            object? state = _stateField!.GetValue(_owner);
            if (state == null) return;
            object? raw = _getValue!.Invoke(state, null);

            // Free-standing: the spot has no native menu to merge into, so SET our own single-action menu (like the
            // raid door trigger). The look-angle gate in ShouldShow keeps this from clobbering nearby area menus.
            if (FreeStanding)
            {
                if (raw is ActionsReturnClass cur && cur.Actions != null)
                    foreach (ActionsTypesClass a in cur.Actions)
                        if (a != null && a.Name == PromptText) { _merged = true; return; }
                ActionsReturnClass fresh = new ActionsReturnClass();
                fresh.Actions.Add(new ActionsTypesClass { Name = PromptText, Action = FireVisit });
                fresh.InitSelected();
                _setValue!.Invoke(state, new object[] { fresh });
                _merged = true;
                return;
            }

            if (!(raw is ActionsReturnClass native) || native.Actions == null)
            {
                // No native area menu up → nothing to merge into.
                _merged = false;
                _nativeFirstSeenFrame = -1;
                return;
            }
            foreach (ActionsTypesClass a in native.Actions)
            {
                if (a != null && a.Name == PromptText) { _merged = true; return; } // already merged in
            }
            // One-frame handshake so we don't race EFT's own set_Value the frame the menu first appears.
            if (_nativeFirstSeenFrame < 0) { _nativeFirstSeenFrame = Time.frameCount; return; }
            if (Time.frameCount == _nativeFirstSeenFrame) return;
            _nativeFirstSeenFrame = -1;

            ActionsReturnClass combined = new ActionsReturnClass();
            foreach (ActionsTypesClass a in native.Actions) combined.Actions.Add(a);
            combined.Actions.Add(new ActionsTypesClass { Name = PromptText, Action = FireVisit });
            combined.InitSelected();
            _setValue!.Invoke(state, new object[] { combined });
            _merged = true;
        }

        private void Hide()
        {
            _nativeFirstSeenFrame = -1;
            if (!_merged) return;
            _merged = false;
            object? state = _stateField?.GetValue(_owner);
            if (state == null) return;
            try
            {
                // Remove ONLY our action; leave the native menu intact (a native interaction may have replaced ours).
                object? raw = _getValue!.Invoke(state, null);
                if (!(raw is ActionsReturnClass existing) || existing.Actions == null) return;
                bool hadOurs = false;
                ActionsReturnClass cleaned = new ActionsReturnClass();
                foreach (ActionsTypesClass a in existing.Actions)
                {
                    if (a != null && a.Name == PromptText) { hadOurs = true; continue; }
                    cleaned.Actions.Add(a);
                }
                if (!hadOurs) return;
                if (cleaned.Actions.Count > 0) cleaned.InitSelected();
                _setValue!.Invoke(state, new object[] { cleaned.Actions.Count > 0 ? (object)cleaned : null! });
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
                Plugin.Log.LogWarning("[HideoutTrigger] no .dlg for " + TraderId);
                return;
            }
            Plugin.Log.LogInfo("[HideoutTrigger] interact -> opening dialog " + TraderId + " at node '" + (Node ?? "(default)") + "'");
            if (DialogOpener.TryOpen(TraderId, out string error))
                StartCoroutine(string.IsNullOrEmpty(Node) ? DialogRunner.Begin(tree) : DialogRunner.BeginAt(tree, Node!));
            else
                Plugin.Log.LogWarning("[HideoutTrigger] open failed: " + error);
        }

        private void OnDestroy() => Hide();
    }
}
