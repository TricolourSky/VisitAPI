using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace VisitAPI.Native;

/// <summary>
/// 包里渲不出来的 shader 按名字换成游戏里的真 shader。两条路：材质一条、组件的 Shader 字段一条
/// （`AmbientLight` 有 5 个这种字段，空壳会让整套环境光崩掉，坑 #87）。
///
/// **能渲的一律不碰。** 2026-09-05 大清理（坑 #122 之后）拆掉的东西：
/// ① `ShaderSource=game` —— 把包里的 1.1 二进制 shader 换成 0.16 同名的，立论是「1.1 shader 发白的嫌疑」；
/// ② `FixKeywords` —— 按「有贴图就开」重开 `_EMISSION`/`_NORMALMAP`，现在打包侧已按 1.1 原件逐材质照抄关键字表，
///    运行时再改就是覆盖 1.1 的授权值；
/// ③ Unlit→自发光 / 雾片关闭 / 头发兜底 / 默认材质白块 —— 全是「补偿模式」专用，那条路已随 #122 一起作废。
/// 发白的真因是网格法线被打包剥光，见 Dev_Note #122。
/// </summary>
public static class SceneShaders
{
    static Dictionary<string, Shader> _shaders;
    static readonly HashSet<string> _missed = new(StringComparer.Ordinal);
    static readonly Dictionary<Type, FieldInfo[]> _shaderFields = new();
    static int _fieldSwaps, _decalSwaps, _decalKept;
    static readonly HashSet<string> _gameSwaps = new(StringComparer.Ordinal);
    static readonly HashSet<string> _noGameTwin = new(StringComparer.Ordinal);

    /// 09-05 从「拆除」改回「保留」（坑 #124）：`game` 是实机验收过的那一档。
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
                if (mat.shader.isSupported && !GameShaders) continue;       // ShaderSource=bundle：能渲的按包里的来
                if (mat.shader.isSupported)                                 // ShaderSource=game：同名一律换成 0.16 自己的
                {
                    if (_shaders.TryGetValue(mat.shader.name, out var own) && own != null && own != mat.shader)
                    { Swap(mat, own); _gameSwaps.Add(own.name); }
                    else _noGameTwin.Add(mat.shader.name);
                    continue;
                }
                if (_shaders.TryGetValue(mat.shader.name, out var real) && real != null) { Swap(mat, real); continue; }
                _missed.Add(mat.shader.name);                               // 换不到真 shader：关掉渲染器，别渲成白板（坑 #92）
                rend.enabled = false;
                break;
            }
        FixFields(root);
    }

    /// <summary>换 shader 时保住材质自己的渲染队列（09-07）。Unity 给材质赋新 shader 会把 renderQueue 重置成新 shader 的默认值：
    /// Mechanic 的镜片 `glasses2`（Standard 透明模式）1.1 授权的是队列 3000（透明阶段），换成 0.16 的 Standard 后掉到 2000（不透明阶段），
    /// 探针日志实锤（队列=2000、_Mode=3、ZWrite=0）——一块关了深度写入的透明片被塞进不透明阶段画，实机就是一片死黑。
    /// 队列是材质数据不是 shader 数据，换 shader 不该动它。</summary>
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
            // 换完要逼组件重新初始化：它可能已经拿空壳建过材质，光换字段不会自己重来
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

    /// 贴花材质不挂 Renderer、挂在 StaticDeferredDecal 上，而贴花管理器在注册那一刻就 `new Material` 把 shader 复制走了——
    /// `Fix` 跑在场景加载之后追不上，所以由 DecalGuard 在注册前调用（坑 #113）。
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
