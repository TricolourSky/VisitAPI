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
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("VisitAPI.art." + file);
        if (stream == null) { Plugin.Log.LogWarning("[art] embedded resource missing: " + file); return _cache[file] = null; }
        var bytes = new byte[stream.Length];
        int off = 0;
        while (off < bytes.Length)
        {
            int n = stream.Read(bytes, off, bytes.Length - off);
            if (n <= 0) break;
            off += n;
        }
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(bytes);
        return _cache[file] = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect, border);
    }
}
