using System;
using System.Collections.Generic;
using System.IO;

namespace VisitAPI.Dialog;

public class DialogLoader
{
    public readonly string BaseDir;
    readonly Dictionary<string, (DateTime mtime, DialogTree tree)> _cache = new();

    public DialogLoader(string baseDir)
    {
        BaseDir = baseDir;
        Directory.CreateDirectory(baseDir);
    }

    public IEnumerable<string> TraderIds()
    {
        foreach (var f in Directory.GetFiles(BaseDir, "*.dlg"))
        {
            var id = Path.GetFileNameWithoutExtension(f);
            if (id.Length == 24) yield return id;
        }
    }

    public DialogTree Load(string traderId)
    {
        var path = Path.Combine(BaseDir, traderId + ".dlg");
        if (!File.Exists(path)) return null;
        var mtime = File.GetLastWriteTimeUtc(path);
        if (_cache.TryGetValue(traderId, out var c) && c.mtime == mtime) return c.tree;
        var tree = DialogParser.Parse(File.ReadAllText(path), traderId);
        foreach (var w in tree.Warnings) Plugin.Log.LogWarning($"[dlg {traderId}] {w}");
        _cache[traderId] = (mtime, tree);
        return tree;
    }
}
