using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using VisitAPI.Native;

namespace VisitAPI
{
    internal sealed class RaidVisitTrigger : MonoBehaviour
    {
        internal string TraderId = "";
        internal string PromptText = "";
        internal Vector3 TriggerPosition;
        internal float MaxDistance = 3f;
        internal float HitRadius = 1.2f;

        private static Type? _gpoType;

        private UnityEngine.Object? _gpo;
        private FieldInfo? _stateField;
        private MethodInfo? _setValue;
        private Camera? _cam;
        private bool _shown;
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
            if ((UnityEngine.Object)(object)_gpo != (UnityEngine.Object)null) return _setValue != null;
            if (Time.unscaledTime < _findAt) return false;
            _findAt = Time.unscaledTime + 1f;
            if (_gpoType == null) _gpoType = AccessTools.TypeByName("EFT.GamePlayerOwner");
            if (_gpoType == null) return false;
            _gpo = UnityEngine.Object.FindObjectOfType(_gpoType);
            if ((UnityEngine.Object)(object)_gpo == (UnityEngine.Object)null) return false;
            _stateField = _gpo.GetType().GetField("AvailableInteractionState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object? state = _stateField?.GetValue(_gpo);
            _setValue = state?.GetType().GetMethod("set_Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            bool ok = _setValue != null;
            Plugin.Log.LogInfo("[RaidTrigger] AvailableInteractionState bind " + (ok ? "PASS" : "FAIL") + " (" + TraderId + ")");
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
            Vector3 to = TriggerPosition - ct.position;
            float angle = Vector3.Angle(ct.forward, to.normalized);
            float maxAngle = Mathf.Clamp(Mathf.Atan2(HitRadius, Mathf.Max(dist, 0.5f)) * Mathf.Rad2Deg, 8f, 40f);
            return angle <= maxAngle;
        }

        private void Show()
        {
            if (_shown) return;
            object? state = _stateField!.GetValue(_gpo);
            if (state == null) return;
            ActionsReturnClass actions = new ActionsReturnClass();
            actions.Actions.Add(new ActionsTypesClass { Name = PromptText, Action = FireVisit });
            actions.InitSelected();
            _setValue!.Invoke(state, new object[] { actions });
            _shown = true;
        }

        private void Hide()
        {
            if (!_shown) return;
            _shown = false;
            object? state = _stateField?.GetValue(_gpo);
            if (state == null) return;
            try { _setValue!.Invoke(state, new object[1]); }
            catch { }
        }

        private void FireVisit()
        {
            Hide();
            _cooldownUntil = Time.unscaledTime + 1.5f;
            DialogTree? tree = DialogTreeLoader.Load(TraderId);
            if (tree == null)
            {
                Plugin.Log.LogWarning("[RaidTrigger] no .dlg for " + TraderId);
                return;
            }
            Plugin.Log.LogInfo("[RaidTrigger] interact -> opening dialog " + TraderId);
            if (DialogOpener.TryOpen(TraderId, out string error))
                StartCoroutine(DialogRunner.Begin(tree));
            else
                Plugin.Log.LogWarning("[RaidTrigger] open failed: " + error);
        }

        private void OnDestroy() => Hide();
    }
}
