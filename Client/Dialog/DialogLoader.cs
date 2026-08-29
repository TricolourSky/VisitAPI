using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VisitAPI.Dialog;

public class DialogLoader
{
    public readonly string BaseDir;
    readonly Dictionary<string, (DateTime mtime, DialogTree tree)> _cache = new();
    List<string> _ids; DateTime _idsStamp;

    public DialogLoader(string baseDir)
    {
        BaseDir = baseDir;
        Directory.CreateDirectory(baseDir);
    }

    /// 目录里所有商人 id；目录 mtime 没变就不重新列文件（章节屏每行目标都要问一遍，扫目录太贵）
    public IEnumerable<string> TraderIds()
    {
        var stamp = Directory.GetLastWriteTimeUtc(BaseDir);
        if (_ids == null || stamp != _idsStamp)
        {
            _idsStamp = stamp;
            _ids = Directory.GetFiles(BaseDir, "*.dlg").Select(Path.GetFileNameWithoutExtension).Where(id => id.Length == 24).ToList();
        }
        return _ids;
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
