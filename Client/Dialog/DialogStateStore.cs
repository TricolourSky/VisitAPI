using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace VisitAPI.Dialog;

public class DialogStateStore
{
    readonly string _path;
    readonly HashSet<string> _keys = new();

    public DialogStateStore(string dir, string traderId)
    {
        _path = Path.Combine(dir, traderId + ".seen.json");
        if (!File.Exists(_path)) return;
        foreach (Match m in Regex.Matches(File.ReadAllText(_path), "\"((?:[^\"\\\\]|\\\\.)*)\"\\s*:"))
            _keys.Add(m.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\"));
    }

    public bool SeenFirst(string profileId) => _keys.Contains(profileId + "|first");
    public void MarkFirst(string profileId) => Add(profileId + "|first");
    public bool OnceUsed(string profileId, string node, int option) => _keys.Contains($"{profileId}|once|{node}|{option}");
    public void MarkOnce(string profileId, string node, int option) => Add($"{profileId}|once|{node}|{option}");

    void Add(string key)
    {
        if (!_keys.Add(key)) return;
        var sb = new StringBuilder("{");
        foreach (var k in _keys)
            sb.Append(sb.Length > 1 ? "," : "").Append("\n  \"").Append(k.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\": 1");
        File.WriteAllText(_path, sb.Append("\n}").ToString());
    }
}
