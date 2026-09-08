using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace VisitAPI.Dialog;

/// <summary>`&lt;traderId&gt;.seen.json`：first/once/trig 的持久化记号（按 profileId 区分，跨档案安全）。
/// 格式 `{ "记号键": 1, ... }`——1.2.1 起的存档兼容格式，读写都走 Newtonsoft（阶段四甩掉了手搓正则解析）。</summary>
public class DialogStateStore
{
    readonly string _path;
    readonly HashSet<string> _keys = new();

    public DialogStateStore(string dir, string traderId)
    {
        _path = Path.Combine(dir, traderId + ".seen.json");
        if (!File.Exists(_path)) return;
        try
        {
            var map = JsonConvert.DeserializeObject<Dictionary<string, int>>(File.ReadAllText(_path));
            if (map != null) foreach (var k in map.Keys) _keys.Add(k);
        }
        catch (System.Exception e) { VisitAPI.Plugin.Log.LogWarning($"[seen] {_path} 解析失败（记号当空处理）: {e.Message}"); }
    }

    public bool SeenFirst(string profileId) => _keys.Contains(profileId + "|first");
    public void MarkFirst(string profileId) => Add(profileId + "|first");
    public bool OnceUsed(string profileId, string node, int option) => _keys.Contains($"{profileId}|once|{node}|{option}");
    public void MarkOnce(string profileId, string node, int option) => Add($"{profileId}|once|{node}|{option}");
    public bool TriggerUsed(string profileId, string key) => _keys.Contains($"{profileId}|trig|{key}");
    public void MarkTrigger(string profileId, string key) => Add($"{profileId}|trig|{key}");

    void Add(string key)
    {
        if (!_keys.Add(key)) return;
        var map = new Dictionary<string, int>();
        foreach (var k in _keys) map[k] = 1;
        File.WriteAllText(_path, JsonConvert.SerializeObject(map, Formatting.Indented));
    }
}
