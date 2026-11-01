using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace VisitAPI.Dialog;

/// <summary>剧本目录（config\VisitAPI\*.dlg）的读取：按修改时间缓存解析结果，文件一改下次 Load 就重读。
/// 10-24：同一帧内不重复查文件系统——金色电话的检查每 1.5 秒对每条对话任务都要翻一遍全部剧本（VisitReach），
/// 剧本改了下一帧照样能察觉。只在主线程用（Time.frameCount）。</summary>
public class DialogLoader
{
    public readonly string BaseDir;
    readonly Dictionary<string, (DateTime mtime, DialogTree tree, int frame)> _cache = new();
    List<string> _ids;
    DateTime _idsStamp;
    int _idsFrame = -1;

    public DialogLoader(string baseDir)
    {
        BaseDir = baseDir;
        Directory.CreateDirectory(baseDir);
    }

    public IEnumerable<string> TraderIds()
    {
        var frame = Time.frameCount;
        if (_ids != null && _idsFrame == frame) return _ids;
        _idsFrame = frame;
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
        var frame = Time.frameCount;
        if (_cache.TryGetValue(traderId, out var c) && c.frame == frame) return c.tree;
        var path = Path.Combine(BaseDir, traderId + ".dlg");
        if (!File.Exists(path)) { _cache[traderId] = (default, null, frame); return null; }
        var mtime = File.GetLastWriteTimeUtc(path);
        if (c.tree != null && c.mtime == mtime) { _cache[traderId] = (mtime, c.tree, frame); return c.tree; }
        var tree = DialogParser.Parse(File.ReadAllText(path), traderId);
        foreach (var w in tree.Warnings) Plugin.Log.LogWarning($"[dlg {traderId}] {w}");
        _cache[traderId] = (mtime, tree, frame);
        return tree;
    }
}
