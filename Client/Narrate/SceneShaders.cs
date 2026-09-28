using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace VisitAPI.Native;

public static class SceneShaders
{
    static Dictionary<string, Shader> _shaders;
    static readonly HashSet<string> _missed = new(StringComparer.Ordinal);
    static readonly Dictionary<Type, FieldInfo[]> _shaderFields = new();

    // 09-25：原来的设置项 Narrate.ShaderSource（默认 game）去掉了，固定用游戏自带的同名着色器
    const bool GameShaders = true;

    public static void Snapshot()
    {
        if (_shaders != null) return;
        _shaders = new Dictionary<string, Shader>(StringComparer.Ordinal);
        foreach (var s in Resources.FindObjectsOfTypeAll<Shader>())
            if (s != null && !string.IsNullOrEmpty(s.name) && !_shaders.ContainsKey(s.name)) _shaders.Add(s.name, s);
    }

    public static void Fix(GameObject root)
    {
        foreach (var rend in root.GetComponentsInChildren<Renderer>(true))
            foreach (var mat in rend.sharedMaterials)
            {
                if (mat == null || mat.shader == null) continue;
                if (mat.shader.isSupported && !GameShaders) continue;
                if (mat.shader.isSupported)
                {
                    if (_shaders.TryGetValue(mat.shader.name, out var own) && own != null && own != mat.shader) Swap(mat, own);
                    continue;
                }
                if (_shaders.TryGetValue(mat.shader.name, out var real) && real != null) { Swap(mat, real); continue; }
                _missed.Add(mat.shader.name);
                rend.enabled = false;
                break;
            }
        FixFields(root);
    }

    static void Swap(Material mat, Shader to)
    {
        var queue = mat.renderQueue;
        mat.shader = to;
        if (mat.renderQueue != queue) mat.renderQueue = queue;
    }

    static void FixFields(GameObject root)
    {
        foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            var type = mb.GetType();
            if (!_shaderFields.TryGetValue(type, out var fields)) _shaderFields[type] = fields = ShaderFieldsOf(type);
            var swapped = false;
            foreach (var f in fields)
            {
                if (!(f.GetValue(mb) is Shader cur) || cur == null || cur.isSupported) continue;
                if (!_shaders.TryGetValue(cur.name, out var native) || native == null || native == cur) continue;
                f.SetValue(mb, native);
                swapped = true;
            }
            if (swapped && mb.enabled) { mb.enabled = false; mb.enabled = true; }
        }
    }

    static FieldInfo[] ShaderFieldsOf(Type type)
    {
        var list = new List<FieldInfo>();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (var t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            foreach (var f in t.GetFields(flags))
                if (f.FieldType == typeof(Shader)) list.Add(f);
        return list.ToArray();
    }

    internal static void FixDecal(Material mat)
    {
        if (mat == null || mat.shader == null || _shaders == null || !GameShaders) return;
        if (_shaders.TryGetValue(mat.shader.name, out var own) && own != null && own != mat.shader) mat.shader = own;
    }

    public static void ReportMisses()
    {
        if (_missed.Count == 0) return;
        Plugin.Log.LogWarning("[scene] renderers disabled, shader not in game (" + _missed.Count + "): " + string.Join(", ", _missed));
        _missed.Clear();
    }
}
