using System;
using System.Collections.Generic;
using UnityEngine;

namespace VisitAPI.Native;

public static class SceneShaders
{
    static Dictionary<string, Shader> _shaders;
    static Shader _fallback;
    static readonly HashSet<string> _missed = new(StringComparer.Ordinal);
    static readonly HashSet<string> _swapLogged = new(StringComparer.Ordinal);

    public static void Snapshot()
    {
        if (_shaders != null) return;
        _shaders = new Dictionary<string, Shader>(StringComparer.Ordinal);
        foreach (var s in Resources.FindObjectsOfTypeAll<Shader>())
            if (s != null && !string.IsNullOrEmpty(s.name) && !_shaders.ContainsKey(s.name)) _shaders.Add(s.name, s);
        Plugin.Log.LogDebug("[scene] native shader snapshot: " + _shaders.Count);
    }

    public static void Fix(GameObject root)
    {
        foreach (var rend in root.GetComponentsInChildren<Renderer>(true))
            foreach (var mat in rend.sharedMaterials)
            {
                if (mat == null || mat.shader == null) continue;
                if (mat.shader.name == "Unlit/Texture")
                {
                    ToEmissive(mat);
                    continue;
                }
                if (mat.shader.name == "Particles/VolumetricSmoke")
                {
                    rend.enabled = false;
                    continue;
                }
                if (_shaders.TryGetValue(mat.shader.name, out var native))
                {
                    if (mat.shader != native) mat.shader = native;
                    continue;
                }
                _missed.Add(mat.shader.name);
                var spare = Fallback();
                if (!mat.shader.isSupported && spare != null) mat.shader = spare;
            }
    }

    public static void ReportMisses()
    {
        if (_missed.Count == 0) return;
        Plugin.Log.LogWarning("[scene] shaders not in snapshot (" + _missed.Count + "): " + string.Join(", ", _missed));
        _missed.Clear();
    }

    // Unity 从不把内置 shader 打进 bundle 而本 build 又没有 Unlit/Texture → 背景半球无 shader 可渲。
    // 换原生 Standard: 不透明写深度(景深后处理不糊), 贴图同时走反照率+自发光(自发光变体在就是无光照照片, 不在也有环境光兜底)
    static void ToEmissive(Material mat)
    {
        var std = FindAny(new[] { "Standard", "p0/Standard", "Standard (Specular setup)" });
        if (std == null) return;
        var tex = mat.mainTexture;
        mat.shader = std;
        mat.SetTexture("_MainTex", tex);
        mat.color = Color.white;
        mat.SetTexture("_EmissionMap", tex);
        mat.SetColor("_EmissionColor", Color.white);
        mat.EnableKeyword("_EMISSION");
        if (_swapLogged.Add("Unlit/Texture"))
        {
            var msg = "[scene] backdrop -> emissive " + std.name + " tex=" + (tex == null ? "<null>" : tex.name);
            if (tex == null) Plugin.Log.LogWarning(msg);
            else Plugin.Log.LogDebug(msg);
        }
    }

    static Shader FindAny(string[] names)
    {
        foreach (var n in names)
        {
            if (_shaders.TryGetValue(n, out var s) && s != null) return s;
            var f = Shader.Find(n);
            if (f != null) { _shaders[n] = f; return f; }
        }
        return null;
    }

    static Shader Fallback()
    {
        if (_fallback != null) return _fallback;
        foreach (var name in new[] { "p0/Standard", "Standard", "Standard (Specular setup)" })
            if (_shaders.TryGetValue(name, out var s) && s != null && s.isSupported) { _fallback = s; return _fallback; }
        return null;
    }
}
