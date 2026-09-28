using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace VisitAPI.Native;

public static class PostChain
{
    static readonly List<(Behaviour effect, Action<RenderTexture, RenderTexture> draw, string name)> Effects = new();
    static GameObject _host;
    static float _logAt;

    public static bool Register(Behaviour effect, string name, Action<RenderTexture, RenderTexture> draw)
    {
        if (effect == null) return false;
        effect.enabled = false;
        var field = Traverse.Create(effect).Field("_ssaaPropagator");
        if (!field.FieldExists())
        {
            Plugin.Log.LogWarning($"[narrate] {name}: no _ssaaPropagator field (this build differs from the one investigated), not installing it");
            return false;
        }
        var propagator = effect.GetComponent<SSAAPropagator>();
        if (propagator == null)
        {
            Plugin.Log.LogWarning($"[narrate] No SSAAPropagator on the visit camera, {name} has nowhere to draw, not installing it");
            return false;
        }
        field.SetValue(propagator);
        if (_host != effect.gameObject) { Effects.Clear(); _host = effect.gameObject; }
        Effects.Add((effect, draw, name));
        return true;
    }

    public static void Clear()
    {
        foreach (var (effect, _, name) in Effects)
        {
            if (effect == null) continue;
            try
            {
                if (effect is UltimateBloom bloom) bloom.ForceShadersReload();
                var mat = Traverse.Create(effect).Field("m_Material");
                if (mat.FieldExists() && mat.GetValue() is Material m) { UnityEngine.Object.DestroyImmediate(m); mat.SetValue(null); }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[narrate] {name} material release failed: {e.Message}"); }
        }
        Effects.Clear();
        _host = null;
    }

    [HarmonyPatch(typeof(SSAAImpl), "OnRenderImage")]
    public static class Before
    {
        static void Prefix(SSAAImpl __instance, RenderTexture source, RenderTexture destination)
        {
            if (Effects.Count == 0 || _host == null || __instance.gameObject != _host) return;
            foreach (var (effect, draw, name) in Effects)
            {
                if (effect == null) continue;
                try { draw(source, destination); }
                catch (Exception e)
                {
                    if (Time.unscaledTime < _logAt) continue;
                    _logAt = Time.unscaledTime + 5f;
                    Plugin.Log.LogError($"[narrate] {name} failed to draw in the ping-pong buffer (skipping this frame): {e}");
                }
            }
        }
    }
}
