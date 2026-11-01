using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Tables;
using VisitAPI.Packs;

namespace VisitAPI.Server;

/// <summary>10-25 SORA「模组作者肯定会用别的什么BOT」「万一别的作者完全不用任何依赖模组呢。就纯API」——AI 队友用什么兵、什么难度、叫什么、
/// 提示里怎么称呼，由剧情包在自己的 pack.json 里写（编辑器只在新建包时写一次 pack.json，之后不改写，加字段是安全的）：
/// <code>
///   "escort": {
///     "role": "exUsec",                          兵种：游戏的 WildSpawnType 名；模组加的兵种照写（比如 blackDivAssault）
///     "difficulty": "normal",                    easy / normal / hard / impossible
///     "names": ["Ghost", "Raven"],               写死的代号（写了就只从这里抽，不够四个拿内置的补）
///     "nameTypes": ["exusec"],                   从这些兵种的名字表里抽（SPT bots\types 的文件名）；不写就用 role 自己的
///     "excludeNames": ["Abyss"],                 名字表里不要的（比如剧情人物）
///     "label": { "ch": "队友", "en": "Teammates" }   就位 / 指挥提示里的称呼（也可以只写一个字符串）
///   }
/// </code>
/// 全部可选。没有包写就是游戏自带的 Rogue（exUsec），不需要任何模组。几个包都写了：按包名排序第一个写的算数，其余的警告一次。
/// 解锁和下单照旧走档案变量 escort_max / escort（EscortVars）。</summary>
public sealed class EscortConfig
{
    public const string DefaultRole = "exUsec";
    static readonly string[] Difficulties = { "easy", "normal", "hard", "impossible" };

    public string Role = DefaultRole;
    public string Difficulty = "normal";
    public List<string> Names = new();
    public List<string> NameTypes = new();
    public HashSet<string> ExcludeNames = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Label = new(StringComparer.OrdinalIgnoreCase);
    public string Pack;   // 哪个包写的；null = 没有包写，用默认

    static EscortConfig _current;
    static readonly object Gate = new();

    /// 当前设置（第一次用到时读，之后一直是同一份）
    public static EscortConfig Current => Load<object>(null);

    public static EscortConfig Load<T>(ISptLogger<T> log)
    {
        lock (Gate)
        {
            if (_current != null) return _current;
            EscortConfig found = null;
            foreach (var p in ContentPacks.All(log))
            {
                var file = Path.Combine(p.Folder, PackLayout.PackJson);
                if (!File.Exists(file)) continue;
                try
                {
                    using var doc = PackJson.Parse(file);
                    if (!doc.RootElement.TryGetProperty("escort", out var e) || e.ValueKind != JsonValueKind.Object) continue;
                    if (found != null)
                    {
                        log?.Warning($"[VisitAPI] Pack {p.Name} also sets teammates (\"escort\" in pack.json), but {found.Pack} already does; {p.Name}'s setting is ignored");
                        continue;
                    }
                    found = Parse(e, p.Name);
                }
                catch (Exception ex) { log?.Error($"[VisitAPI] Pack {p.Name}: \"escort\" in pack.json could not be read, ignored: {ex.Message}"); }
            }
            return _current = found ?? new EscortConfig();
        }
    }

    static EscortConfig Parse(JsonElement e, string pack)
    {
        var c = new EscortConfig { Pack = pack };
        if (Str(e, "role") is { } role) c.Role = role;
        if (Str(e, "difficulty") is { } dif) c.Difficulty = dif.ToLowerInvariant();
        c.Names = List(e, "names");
        c.NameTypes = List(e, "nameTypes");
        foreach (var n in List(e, "excludeNames")) c.ExcludeNames.Add(n);
        if (e.TryGetProperty("label", out var l))
        {
            if (l.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(l.GetString()))
                c.Label["ch"] = c.Label["en"] = l.GetString().Trim();
            else if (l.ValueKind == JsonValueKind.Object)
                foreach (var kv in l.EnumerateObject())
                    if (kv.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(kv.Value.GetString())) c.Label[kv.Name] = kv.Value.GetString().Trim();
        }
        return c;
    }

    static string Str(JsonElement o, string key) =>
        o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString().Trim() : null;

    static List<string> List(JsonElement o, string key)
    {
        var list = new List<string>();
        if (o.TryGetProperty(key, out var a) && a.ValueKind == JsonValueKind.Array)
            foreach (var x in a.EnumerateArray())
                if (x.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(x.GetString())) list.Add(x.GetString().Trim());
        return list;
    }

    /// 名字从哪些名字表里抽：包里写了 nameTypes 就按它；没写就用 role 自己的（SPT 的 bots\types 文件名就是小写的兵种名，
    /// PMC 的名字在 usec / bear 两张表里）
    public List<string> NameTables()
    {
        if (NameTypes.Count > 0) return NameTypes.Select(t => t.ToLowerInvariant()).ToList();
        var key = Role.ToLowerInvariant();
        return key switch { "pmcusec" => new() { "usec", key }, "pmcbear" => new() { "bear", key }, _ => new() { key } };
    }

    public bool KnownDifficulty => Difficulties.Contains(Difficulty, StringComparer.OrdinalIgnoreCase);
}

/// <summary>开服时读一次，有问题的在开服日志里报（兵种在服务端的数据库里没有、难度写错、几个包都写了）。路由第一次用到时拿的是同一份</summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public class EscortConfigLoader(BotTable bots, ISptLogger<EscortConfigLoader> log) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var c = EscortConfig.Load(log);
        if (c.Pack != null)
        {
            if (bots?.Types != null && !bots.Types.ContainsKey(c.Role.ToLowerInvariant()))
                log.Warning($"[VisitAPI] Pack {c.Pack}: teammate bot type '{c.Role}' is not in the server's bot database (is the mod that adds it installed?); teammates will not spawn");
            if (!c.KnownDifficulty)
                log.Warning($"[VisitAPI] Pack {c.Pack}: unknown teammate difficulty '{c.Difficulty}' (easy / normal / hard / impossible); teammates spawn on normal");
        }
        return Task.CompletedTask;
    }
}
