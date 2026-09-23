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
    static int _fieldSwaps, _decalSwaps, _decalKept;
    static readonly HashSet<string> _gameSwaps = new(StringComparer.Ordinal);
    static readonly HashSet<string> _noGameTwin = new(StringComparer.Ordinal);

    static bool GameShaders => string.Equals(Plugin.ShaderSource.Value.Trim(), "game", StringComparison.OrdinalIgnoreCase);

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
                if (mat.shader.isSupported && !GameShaders) continue;
                if (mat.shader.isSupported)
                {
                    if (_shaders.TryGetValue(mat.shader.name, out var own) && own != null && own != mat.shader)
                    { Swap(mat, own); _gameSwaps.Add(own.name); }
                    else _noGameTwin.Add(mat.shader.name);
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
        if (mat.renderQueue != queue)
        {
            mat.renderQueue = queue;
            _queueKept++;
        }
    }
    static int _queueKept;

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
            if (!swapped) continue;
            _fieldSwaps++;
            if (mb.enabled) { mb.enabled = false; mb.enabled = true; }
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
        if (_shaders.TryGetValue(mat.shader.name, out var own) && own != null && own != mat.shader) { mat.shader = own; _decalSwaps++; }
        else if (own == null) _decalKept++;
    }

    public static void ReportMisses()
    {
        if (_gameSwaps.Count > 0 || _noGameTwin.Count > 0)
        {
            Plugin.Log.LogInfo($"[scene] ShaderSource=game：换成 0.16 同名 shader {_gameSwaps.Count} 种 [{string.Join(", ", _gameSwaps)}]；0.16 没有同名、仍用包内 1.1 二进制 {_noGameTwin.Count} 种 [{string.Join(", ", _noGameTwin)}]；换 shader 时保住渲染队列 {_queueKept} 个材质");
            _gameSwaps.Clear(); _noGameTwin.Clear(); _queueKept = 0;
        }
        if (_decalSwaps > 0 || _decalKept > 0)
        {
            Plugin.Log.LogInfo($"[scene] 贴花 shader: 注册前换成 0.16 同名 {_decalSwaps} 个材质，0.16 无同名仍用 1.1 的 {_decalKept} 个（坑 #113）");
            _decalSwaps = _decalKept = 0;
        }
        if (_fieldSwaps > 0)
        {
            Plugin.Log.LogDebug("[scene] shader fields fixed on " + _fieldSwaps + " components");
            _fieldSwaps = 0;
        }
        if (_missed.Count == 0) return;
        Plugin.Log.LogWarning("[scene] renderers disabled, shader not in game (" + _missed.Count + "): " + string.Join(", ", _missed));
        _missed.Clear();
    }
}
