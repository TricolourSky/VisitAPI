using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using EFT;
using EFT.AnimationSequencePlayer;
using EFT.Dialogs;
using EFT.Quests;
using EFT.UI;
using VisitAPI.Dialog;

namespace VisitAPI.Native;

public static class DialogTemplateBuilder
{
    public static readonly Dictionary<MongoID, string> BgByDialog = new();
    public static readonly Dictionary<MongoID, string> VoiceByDialog = new();
    public static readonly Dictionary<MongoID, string> BgmByDialog = new();
    public static readonly Dictionary<MongoID, string> NodeByDialog = new();

    public static readonly Dictionary<MongoID, string> NarrationByDialog = new();

    /// 每个节点的「商人说话」那一段（#npc）。它只有一句商人台词、没有玩家选项，一出现就立刻跳到选项段，见 SayBeatWindow
    public static readonly HashSet<MongoID> SayDialogs = new();

    /// 10-16：每段对话属于哪家剧本（商人 id）。跨剧本跳转后 SpeakerSwitch 靠它把对话窗的名字 / 头像换成说话的那家
    public static readonly Dictionary<MongoID, string> TraderByDialog = new();
    static readonly Dictionary<string, DialogTree> _linked = new();
    static readonly HashSet<string> _registering = new();
    /// 这一次开对话一起注册进来的所有商人（本家 + 跨剧本引用到的）。每家模板的 SubTraders 填上其余几家：
    /// 引擎每执行一行都走 method_5 → InitNewDialog(屏幕的商人, 对话 id) → template.HasTraderId(屏幕的商人)，
    /// 跳到别家后屏幕的商人还是本家，不在对方模板的 MainTrader / SubTraders 里就报 "Trader … not found in dialog" 直接关掉对话（10-16 实机「跳转没成功」）
    static readonly HashSet<string> _group = new();

    /// 开对话时解析过的剧本（本家 + `-> 商人id:节点` 引用到的别家），OptionMap / QuestRefresh 拿它免得每个选项重读一遍文件
    public static DialogTree Linked(string traderId) => traderId != null && _linked.TryGetValue(traderId, out var t) ? t : null;

    /// 最近一次开对话一起注册进来的全部商人 id（本家 + 多级跨剧本引用到的）；QuestRefresh 用它盯住多级跳转里用到的任务（10-24）
    public static IReadOnlyCollection<string> Group => _group;

    public static MongoID Register(DialogTree tree, string entryNode, string startNode, string playerName, QuestController quests)
    {
        if (_registering.Count == 0) _group.Clear();
        _group.Add(tree.TraderId);
        _linked[tree.TraderId] = tree;
        _registering.Add(tree.TraderId);
        try
        {
            LinkExternal(tree, playerName, quests);
            return Build(tree, entryNode, startNode, playerName, quests);
        }
        finally { _registering.Remove(tree.TraderId); }
    }

    /// 10-16 SORA（通话被 Abyss 劫持）：本剧本里 `-> 商人id:节点` 指到的别家剧本也一并注册进 DialogStorage，不然点下去 method_0 找不到模板。
    /// 正在注册中的那家不再进（A→B→A 不打转）；别家剧本按它自己的 start 起手
    static void LinkExternal(DialogTree tree, string playerName, QuestController quests)
    {
        var others = tree.Nodes.Values.SelectMany(n => n.Options)
            .Select(o => DialogParser.IsExternal(o.Target, out var id, out _) ? id : null)
            .Where(id => id != null && id != tree.TraderId).Distinct().ToList();
        foreach (var traderId in others)
        {
            if (_registering.Contains(traderId)) continue;
            var other = DialogFiles.Tree(traderId);
            if (other == null) { Plugin.Log.LogWarning($"[dialog] {tree.TraderId}: jumps to trader {traderId}, which has no .dlg; those options will close the dialogue"); continue; }
            Register(other, other.Start, other.Start, playerName, quests);
        }
    }

    static MongoID Build(DialogTree tree, string entryNode, string startNode, string playerName, QuestController quests)
    {
        WhitelistPatch.RegisteredTraders.Add(tree.TraderId);
        var loc = new LocTable(playerName);   // 默认文字 + 各语言译文（.dlg 的译文行），09-23
        var built = new List<(MongoID id, List<DialogLineTemplate> lines)>();
        foreach (var node in tree.Nodes.Values)
        {
            Beats(tree, node, loc, built);
            var rows = new List<DialogLineTemplate>();
            var open = false;
            var gated = new List<DialogMainConditionGroup>();
            for (var i = 0; i < node.Options.Count; i++)
            {
                var o = node.Options[i];
                var lineId = Id(tree.TraderId, node.Name + "#" + i);
                // confirm: 键名 [-> 节点]（10-15）：这一行点下去先弹关键抉择窗，NarrateChoiceWindow 按行 id 查
                if (o.ConfirmKey != null)
                    DialogConfirm.Put(lineId.ToString(), o.ConfirmKey, o.ConfirmNo != null && tree.Nodes.ContainsKey(o.ConfirmNo) ? OptionMap.Entry(tree, o.ConfirmNo) : (MongoID?)null);
                var e = Effect(tree, node, o, i, lineId);
                var once = o.Once ? new VariableValueCondition(OnceService.OnceId(tree.TraderId, node.Name, i), 0) : null;
                var trigger = QuestGates.Trigger(o, quests, once);
                open |= trigger == null;
                if (trigger != null) gated.Add(trigger);
                var main = OptionMap.Act(tree, startNode, o.Target);
                if (node.NpcSlot < node.Narration.Count) main = new DialogSwitchDialogAction(Epilogue(tree, node, i, main, e, loc, built));
                rows.Add(Line(lineId, EDialogSide.Player, OptionMap.Icon(o), QuestGates.Actions(o, main), trigger, loc.Put(tree.TraderId, node.Name, "opt" + i, o.Text, o.Tr)));
            }
            // 所有选项都带条件的节点留一个「（结束）」当出口；10-02 起它只在选项此刻一个都点不了时才出现（见 NoOptionOpen）
            if (!open && node.JumpTo == null)
                rows.Add(Line(Id(tree.TraderId, node.Name + "#end"), EDialogSide.Player, DialogLineTemplate.EDialogLineIconType.QuitIcon, new DialogAction[] { new DialogQuitAction() },
                    QuestGates.OnlyIfNoneOpen(gated), loc.Put(tree.TraderId, node.Name, "end", Loc.Pick("（结束）", "(End)"), null)));
            built.Add((Id(tree.TraderId, node.Name + "#opt"), rows));
        }
        // 引擎按 LocalizationManager.Culture 挑表（各语言的表怎么叠见 LocTable）；「当前文化」那张放玩家要的语言（Loc.Code）
        var locByLang = loc.Tables(LocalizationManager.Instance?.Culture);
        var subTraders = _group.Where(id => id != tree.TraderId).Select(id => new MongoID(id)).ToArray();
        foreach (var (id, lines) in built)
        {
            DialogStorage.Instance.AddTemplate(new TraderDialogTemplate(id, new MongoID(tree.TraderId), subTraders, lines, locByLang) { CanBeFirstDialog = true });
            TraderByDialog[id] = tree.TraderId;
        }
        return OptionMap.Entry(tree, entryNode);
    }

    static LineEffect Effect(DialogTree tree, DialogNode node, DialogOption o, int i, MongoID lineId)
    {
        var e = LineEffects.For(lineId);
        e.Standing = o.StandingDelta != 0 ? (o.StandingTraderId ?? tree.TraderId, o.StandingDelta) : null;
        e.SetStatus = o.SetStatusId != null ? (o.SetStatusId, o.SetStatusValue) : null;
        e.SyncVar = o.SetVarName != null ? (Vars.Id(o.SetVarName), o.SetVarValue) : null;
        e.HandoverQuest = o.HandoverId;
        e.Tab = o.Target switch
        {
            "@trade" => TraderScreensGroup.ETraderMode.Trade,
            "@tasks" => TraderScreensGroup.ETraderMode.Tasks,
            "@services" => TraderScreensGroup.ETraderMode.Services,
            _ => null,
        };
        e.Once = o.Once ? (tree.TraderId, node.Name, i) : null;
        return e;
    }

    static void Beats(DialogTree tree, DialogNode node, LocTable loc, List<(MongoID id, List<DialogLineTemplate> lines)> built)
    {
        var slots = new List<int>();
        for (var i = 0; i < node.NpcSlot; i++) slots.Add(i);
        slots.Add(-1);
        var npc = Id(tree.TraderId, node.Name + "#npc");
        var opt = Id(tree.TraderId, node.Name + "#opt");
        NodeByDialog[npc] = node.Name;
        NodeByDialog[opt] = node.Name;
        Put(VoiceByDialog, npc, node.NpcAudio);
        Put(BgmByDialog, SlotId(tree, node, slots[0]), node.Bgm);
        var jump = node.JumpTo != null && tree.Nodes.ContainsKey(node.JumpTo) ? OptionMap.Entry(tree, node.JumpTo) : (MongoID?)null;
        // 跳转的收尾旁白链用选项取不到的序号 -1：节点同时写了 -> 和选项时，和第 0 个选项的链共用 id 的话后注册的赢（10-29）
        var after = jump == null ? opt
                  : node.NpcSlot < node.Narration.Count ? Epilogue(tree, node, -1, new DialogSwitchDialogAction(jump.Value), null, loc, built) : jump.Value;
        for (var p = 0; p < slots.Count; p++)
        {
            var s = slots[p];
            var next = p + 1 < slots.Count ? SlotId(tree, node, slots[p + 1]) : after;
            var bg = s < 0 ? node.Bg : node.Narration[s].Bg ?? node.Bg;
            Put(BgByDialog, SlotId(tree, node, s), bg);
            if (s < 0)
            {
                var say = Line(Id(tree.TraderId, node.Name + "#say"), EDialogSide.Npc, DialogLineTemplate.EDialogLineIconType.DialogBubble,
                    new DialogSwitchDialogAction(next), loc.Put(tree.TraderId, node.Name, "npc", node.NpcText ?? "……", node.NpcTr));
                built.Add((npc, new List<DialogLineTemplate> { say }));
                SayDialogs.Add(npc);
                continue;
            }
            var nar = Id(tree.TraderId, node.Name + "#nar" + s);
            var nc = Id(tree.TraderId, node.Name + "#nc" + s);
            Put(BgByDialog, nc, bg);
            var narKey = loc.Put(tree.TraderId, node.Name, "nar" + s, node.Narration[s].Text, node.Narration[s].Tr);
            NarrationByDialog[nar] = loc.Shown(narKey);
            NarrationByDialog[nc] = loc.Shown(narKey);
            built.Add((nar, new List<DialogLineTemplate> { Line(Id(tree.TraderId, node.Name + "#nl" + s), EDialogSide.Npc, DialogLineTemplate.EDialogLineIconType.DialogBubble, new DialogSwitchDialogAction(nc), narKey) }));
            built.Add((nc, new List<DialogLineTemplate> { Line(Id(tree.TraderId, node.Name + "#ncl" + s), EDialogSide.Player, DialogLineTemplate.EDialogLineIconType.IndexFinger, new DialogSwitchDialogAction(next), loc.Put(tree.TraderId, node.Name, "cont" + s, Loc.Pick("继续…", "Continue..."), null)) }));
            Put(VoiceByDialog, nar, node.Narration[s].Audio);
        }
    }

    static MongoID Epilogue(DialogTree tree, DialogNode node, int option, DialogAction final, LineEffect effect, LocTable loc, List<(MongoID id, List<DialogLineTemplate> lines)> built)
    {
        MongoID BeatId(int s) => Id(tree.TraderId, $"{node.Name}#e{option}_{s}");
        for (var s = node.NpcSlot; s < node.Narration.Count; s++)
        {
            var last = s == node.Narration.Count - 1;
            var id = BeatId(s);
            var lineId = Id(tree.TraderId, $"{node.Name}#el{option}_{s}");
            NodeByDialog[id] = node.Name;
            NarrationByDialog[id] = loc.Shown(loc.Put(tree.TraderId, node.Name, $"e{option}_{s}", node.Narration[s].Text, node.Narration[s].Tr));
            Put(BgByDialog, id, node.Narration[s].Bg ?? node.Bg);
            Put(VoiceByDialog, id, node.Narration[s].Audio);
            built.Add((id, new List<DialogLineTemplate> { Line(lineId, EDialogSide.Player, DialogLineTemplate.EDialogLineIconType.IndexFinger, last ? final : new DialogSwitchDialogAction(BeatId(s + 1)), loc.Put(tree.TraderId, node.Name, $"econt{option}_{s}", Loc.Pick("继续…", "Continue..."), null)) }));
            LineEffects.For(lineId).Tab = last && effect != null ? effect.Tab : null;
        }
        if (effect != null) effect.Tab = null;
        return BeatId(node.NpcSlot);
    }

    static void Put(Dictionary<MongoID, string> map, MongoID id, string file)
    {
        if (file != null) map[id] = file;
        else map.Remove(id);
    }

    static DialogLineTemplate Line(MongoID id, EDialogSide side, DialogLineTemplate.EDialogLineIconType icon, DialogAction act, string key) =>
        Line(id, side, icon, act == null ? null : new[] { act }, null, key);

    static DialogLineTemplate Line(MongoID id, EDialogSide side, DialogLineTemplate.EDialogLineIconType icon, DialogAction[] acts, DialogMainConditionGroup trigger, string key) =>
        new(id, side, icon, trigger, acts, new CombinedAnimationData(new List<AnimationParams>(), new List<AnimationParams>(), new List<LipSyncParams>(), new List<SubtitleParams> { new() { Key = key } }, new MediaData()));

    internal static MongoID SlotId(DialogTree t, DialogNode n, int s) =>
        Id(t.TraderId, n.Name + (s < 0 ? "#npc" : "#nar" + s));

    // 10-24：同样的输入永远同样的 id，按「商人|名字」缓存；SORA 的剧本每开一次对话要算几千次
    static readonly Dictionary<string, MongoID> _ids = new(StringComparer.Ordinal);
    static readonly MD5 Md5 = MD5.Create();

    /// 由文字派生的对话 / 变量 id（MD5 前 12 字节的十六进制）。算法不能改：档案变量（once / 记号 / setVariables 的记号）已经按它存进玩家档案，服务端 Ids.Hex24 算的也是它
    internal static MongoID Id(string traderId, string name)
    {
        var key = traderId + "|" + name;
        lock (_ids)
        {
            if (_ids.TryGetValue(key, out var hit)) return hit;
            var hash = Md5.ComputeHash(Encoding.UTF8.GetBytes(key));
            var sb = new StringBuilder(24);
            for (var i = 0; i < 12; i++) sb.Append(hash[i].ToString("x2"));
            return _ids[key] = new MongoID(sb.ToString());
        }
    }
}
