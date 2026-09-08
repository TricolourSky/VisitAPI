using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace VisitAPI.Native;

public static class VisitArt
{
    static readonly Dictionary<string, Sprite> _cache = new();

    public static Sprite Load(string file, Vector4 border = default)
    {
        if (_cache.TryGetValue(file, out var hit)) return hit;
        var bytes = Bytes(file);
        return _cache[file] = bytes != null ? Decode(bytes, border) : null;
    }

    /// 内嵌的贴图原件（1.1 相机效果要的 ramp / 抖动图）→ Texture2D，带 mip；解不出来返回 null
    public static Texture2D LoadTexture(string file, TextureWrapMode wrap, FilterMode filter, bool linear = false)
    {
        var bytes = Bytes(file);
        if (bytes == null) return null;
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear);
        if (!tex.LoadImage(bytes)) { Object.Destroy(tex); return null; }
        tex.wrapMode = wrap;
        tex.filterMode = filter;
        return tex;
    }

    static byte[] Bytes(string file)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("VisitAPI.art." + file);
        if (stream == null) { Plugin.Log.LogWarning("[art] embedded resource missing: " + file); return null; }
        var bytes = new byte[stream.Length];
        var off = 0;
        while (off < bytes.Length)
        {
            var n = stream.Read(bytes, off, bytes.Length - off);
            if (n <= 0) break;
            off += n;
        }
        return bytes;
    }

    /// PNG/JPG 字节 → Sprite；解不出来返回 null
    public static Sprite Decode(byte[] bytes, Vector4 border = default)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(bytes)) { Object.Destroy(tex); return null; }
        return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect, border);
    }
}
