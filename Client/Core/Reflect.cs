using System;
using System.Reflection;
using UnityEngine;

namespace VisitAPI;

/// <summary>
/// 「按字段名往组件里写值」的公共工具。2026-09-05 合并：`Camera11` 和 `PrismTransplant` 原来各写了一套
/// （一套按点号路径写单个字段、一套整组件照搬序列化字段），做的是同一件事，收在这里一处。
/// </summary>
public static class Reflect
{
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>按字段名写值；`a.b` 形式的路径穿一层嵌套（结构体也行：取出来改完再放回去）。写不进去返回 false。</summary>
    public static bool Set(object target, string path, object value)
    {
        var dot = path.IndexOf('.');
        var f = target.GetType().GetField(dot < 0 ? path : path.Substring(0, dot), Any);
        if (f == null) return false;
        if (dot < 0) return Assign(target, f, value);
        var inner = f.GetValue(target);
        if (inner == null) return false;
        if (!Set(inner, path.Substring(dot + 1), value)) return false;
        f.SetValue(target, inner);   // 结构体是值拷贝，改完必须放回去
        return true;
    }

    /// <summary>同类组件之间照搬序列化字段。Shader / Material / 预设这类引用留目标自己的（贴图跟着搬）。</summary>
    public static int Copy(Component from, Component to, Type type, out int kept)
    {
        int copied = 0;
        kept = 0;
        foreach (var f in type.GetFields(Any))
        {
            if (!Serialized(f) || KeepOurs(f.FieldType)) { kept++; continue; }
            f.SetValue(to, f.GetValue(from));
            copied++;
        }
        return copied;
    }

    static bool Assign(object target, FieldInfo f, object value)
    {
        try
        {
            f.SetValue(target, Convert(f.FieldType, value));
            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[narrate] {target.GetType().Name}.{f.Name} 写不进去（{f.FieldType.Name} <- {value?.GetType().Name}）: {e.GetType().Name}");
            return false;
        }
    }

    static object Convert(Type t, object v)
    {
        if (t.IsEnum) return Enum.ToObject(t, System.Convert.ToInt32(v));
        if (t == typeof(bool)) return v is bool b ? b : System.Convert.ToInt32(v) != 0;
        if (t == typeof(float)) return System.Convert.ToSingle(v);
        if (t == typeof(int)) return System.Convert.ToInt32(v);
        return v;   // 数组 / Color / Vector4：类型一致直接放
    }

    static bool Serialized(FieldInfo f) =>
        !f.IsStatic && !f.IsInitOnly && f.GetCustomAttribute<NonSerializedAttribute>() == null
        && (f.IsPublic || f.GetCustomAttribute<SerializeField>() != null);

    static bool KeepOurs(Type t) =>
        typeof(UnityEngine.Object).IsAssignableFrom(t) && !typeof(Texture).IsAssignableFrom(t);
}
