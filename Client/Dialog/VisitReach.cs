using System;
using System.Collections.Generic;
using System.Linq;
using VisitAPI.Dialog;

namespace VisitAPI.Native;

/// <summary>
/// 10-18 SORA（街区对话结束后访问还是金色）：金色电话 / 金色访问只该指向「点访问真能接 / 交」的任务。
/// 战局、藏身处触发点后面的选项从主菜单访问根本走不到。
/// 这里从访问的进门节点（start: 和 when: 的节点）出发，顺着选项目标、自动跳转、抉择窗「否」、`商人id:节点` 跨剧本跳转收集走得到的选项。
/// 选项上的条件（if: 之类）不看，只看路通不通；剧本文件一改（DialogLoader 给了新对象）整张重算。
/// 10-24：遍历本身抽成 <see cref="Walk(DialogTree, IEnumerable{string}, IReadOnlyDictionary{string, DialogTree})"/>，触发点的 once 判定（VisitTrigger）也走它。
/// </summary>
public static class VisitReach
{
    sealed class Reach { public readonly HashSet<string> Accept = new(), Finish = new(); }

    static List<DialogTree> _trees = new();
    static List<Reach> _reach = new();

    /// <summary>按商人 id 排序，第一家「点访问走得到这条任务的选项」的剧本：accept = 接任务的选项，否则 = 交任务 / 上交的选项。
    /// 跨剧本跳过去的选项算在进门那家头上（玩家点的是那家的访问）。</summary>
    public static DialogTree TraderFor(string questId, bool accept)
    {
        Refresh();
        for (var i = 0; i < _trees.Count; i++)
            if ((accept ? _reach[i].Accept : _reach[i].Finish).Contains(questId)) return _trees[i];
        return null;
    }

    static void Refresh()
    {
        var now = DialogFiles.All().OrderBy(t => t.TraderId, StringComparer.Ordinal).ToList();
        if (now.Count == _trees.Count && now.Zip(_trees, (a, b) => ReferenceEquals(a, b)).All(same => same)) return;
        var byId = ById(now);
        _reach = now.Select(t => Collect(t, byId)).ToList();
        _trees = now;
    }

    /// 商人 id → 剧本，给 Walk 跟跨剧本跳转用
    internal static Dictionary<string, DialogTree> ById(IEnumerable<DialogTree> trees)
    {
        var byId = new Dictionary<string, DialogTree>();
        foreach (var t in trees) byId[t.TraderId] = t;
        return byId;
    }

    static Reach Collect(DialogTree entry, Dictionary<string, DialogTree> byId)
    {
        var reach = new Reach();
        foreach (var (_, node) in Walk(entry, new[] { "@start" }, byId))
            foreach (var o in node.Options)
            {
                reach.Accept.UnionWith(o.AcceptIds);
                reach.Finish.UnionWith(o.CompleteIds);
                if (o.HandoverId != null) reach.Finish.Add(o.HandoverId);
            }
        return reach;
    }

    /// <summary>从这几个入口（节点名、`@start` = start: 加全部 when: 节点、`商人id:节点`）出发走得到的节点，每个只出一次：
    /// 顺着选项目标、自动跳转（->）、抉择窗「否」的去处、跨剧本跳转（byId 里有那家才跟过去）。选项上的条件不看，只看路通不通。</summary>
    internal static IEnumerable<(DialogTree tree, DialogNode node)> Walk(DialogTree entry, IEnumerable<string> entries, IReadOnlyDictionary<string, DialogTree> byId)
    {
        var seen = new HashSet<string>();
        var queue = new Queue<(DialogTree tree, string node)>();
        void Go(DialogTree t, string target)
        {
            if (string.IsNullOrEmpty(target)) return;
            if (DialogParser.IsExternal(target, out var id, out var name)) { if (byId.TryGetValue(id, out var other)) queue.Enqueue((other, name)); }
            else if (target == "@start") { queue.Enqueue((t, t.Start)); foreach (var w in t.WhenRules) queue.Enqueue((t, w.Node)); }
            else if (target[0] != '@') queue.Enqueue((t, target));
        }
        foreach (var e in entries) Go(entry, e);
        while (queue.Count > 0)
        {
            var (t, name) = queue.Dequeue();
            if (name == null || !t.Nodes.TryGetValue(name, out var node) || !seen.Add(t.TraderId + ":" + name)) continue;
            yield return (t, node);
            if (node.JumpTo != null) queue.Enqueue((t, node.JumpTo));
            foreach (var o in node.Options)
            {
                Go(t, o.Target);
                if (o.ConfirmNo != null) queue.Enqueue((t, o.ConfirmNo));
            }
        }
    }
}
