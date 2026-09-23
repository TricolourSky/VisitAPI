using System.Collections.Generic;
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

    public static MongoID Register(DialogTree tree, string entryNode, string startNode, string playerName, QuestController quests)
    {
        WhitelistPatch.RegisteredTraders.Add(tree.TraderId);
        var loc = new LocTable(playerName);   // 默认文字 + 各语言译文（.dlg 的译文行），09-23
        var built = new List<(MongoID id, List<DialogLineTemplate> lines)>();
        foreach (var node in tree.Nodes.Values)
        {
            Beats(tree, node, loc, built);
            var rows = new List<DialogLineTemplate>();
            var open = false;
            for (var i = 0; i < node.Options.Count; i++)
            {
                var o = node.Options[i];
                var lineId = Id(tree.TraderId, node.Name + "#" + i);
                var e = Effect(tree, node, o, i, lineId);
                var once = o.Once ? new VariableValueCondition(OnceService.OnceId(tree.TraderId, node.Name, i), 0) : null;
                var trigger = QuestGates.Trigger(o, quests, once);
                open |= trigger == null;
                var main = OptionMap.Act(tree, startNode, o.Target);
                if (node.NpcSlot < node.Narration.Count) main = new DialogSwitchDialogAction(Epilogue(tree, node, i, main, e, loc, built));
                rows.Add(Line(lineId, EDialogSide.Player, OptionMap.Icon(o), QuestGates.Actions(o, main), trigger, loc.Put(tree.TraderId, node.Name, "opt" + i, o.Text, o.Tr)));
            }
            if (!open && node.JumpTo == null)
                rows.Add(Line(Id(tree.TraderId, node.Name + "#end"), EDialogSide.Player, DialogLineTemplate.EDialogLineIconType.QuitIcon, new DialogQuitAction(), loc.Put(tree.TraderId, node.Name, "end", Loc.Pick("（结束）", "(End)"), null)));
            built.Add((Id(tree.TraderId, node.Name + "#opt"), rows));
        }
        // 引擎按 LocalizationManager.Culture 挑表：每种语言的表 = 默认文字盖上该语言的译文；「当前文化」那张放玩家要的语言（Loc.Code）
        var locByLang = loc.Tables(LocalizationManager.Instance?.Culture);
        foreach (var (id, lines) in built)
            DialogStorage.Instance.AddTemplate(new TraderDialogTemplate(id, new MongoID(tree.TraderId), new MongoID[0], lines, locByLang) { CanBeFirstDialog = true });
        return OptionMap.Entry(tree, entryNode);
    }

    static LineEffect Effect(DialogTree tree, DialogNode node, DialogOption o, int i, MongoID lineId)
    {
        var e = LineEffects.For(lineId);
        e.Standing = o.StandingDelta != 0 ? (o.StandingTraderId ?? tree.TraderId, o.StandingDelta) : ((string, double)?)null;
        e.SetStatus = o.SetStatusId != null ? (o.SetStatusId, o.SetStatusValue) : ((string, int)?)null;
        e.SyncVar = o.SetVarName != null ? (Vars.Id(o.SetVarName), o.SetVarValue) : ((MongoID, int)?)null;
        e.HandoverQuest = o.HandoverId;
        e.Tab = o.Target switch
        {
            "@trade" => TraderScreensGroup.ETraderMode.Trade,
            "@tasks" => TraderScreensGroup.ETraderMode.Tasks,
            "@services" => TraderScreensGroup.ETraderMode.Services,
            _ => (TraderScreensGroup.ETraderMode?)null,
        };
        e.Once = o.Once ? (tree.TraderId, node.Name, i) : ((string, string, int)?)null;
        return e;
    }

    static void Beats(DialogTree tree, DialogNode node, LocTable loc, List<(MongoID id, List<DialogLineTemplate> lines)> built)
    {
        var slots = new List<int>();
        for (var i = 0; i < node.NpcSlot; i++) slots.Add(i);
        slots.Add(-1);
        NodeByDialog[Id(tree.TraderId, node.Name + "#npc")] = node.Name;
        NodeByDialog[Id(tree.TraderId, node.Name + "#opt")] = node.Name;
        Put(VoiceByDialog, Id(tree.TraderId, node.Name + "#npc"), node.NpcAudio);
        Put(BgmByDialog, SlotId(tree, node, slots[0]), node.Bgm);
        var jump = node.JumpTo != null && tree.Nodes.ContainsKey(node.JumpTo) ? OptionMap.Entry(tree, node.JumpTo) : (MongoID?)null;
        var after = jump == null ? Id(tree.TraderId, node.Name + "#opt")
                  : node.NpcSlot < node.Narration.Count ? Epilogue(tree, node, 0, new DialogSwitchDialogAction(jump.Value), null, loc, built) : jump.Value;
        for (var p = 0; p < slots.Count; p++)
        {
            var s = slots[p];
            var next = p + 1 < slots.Count ? SlotId(tree, node, slots[p + 1]) : after;
            var bg = s < 0 ? node.Bg : node.Narration[s].Bg ?? node.Bg;
            Put(BgByDialog, SlotId(tree, node, s), bg);
            if (s >= 0) Put(BgByDialog, Id(tree.TraderId, node.Name + "#nc" + s), bg);
            if (s < 0)
            {
                var say = Line(Id(tree.TraderId, node.Name + "#say"), EDialogSide.Npc, DialogLineTemplate.EDialogLineIconType.DialogBubble,
                    new DialogSwitchDialogAction(next), loc.Put(tree.TraderId, node.Name, "npc", node.NpcText ?? "……", node.NpcTr));
                built.Add((Id(tree.TraderId, node.Name + "#npc"), new List<DialogLineTemplate> { say }));
                continue;
            }
            var narKey = loc.Put(tree.TraderId, node.Name, "nar" + s, node.Narration[s].Text, node.Narration[s].Tr);
            NarrationByDialog[Id(tree.TraderId, node.Name + "#nar" + s)] = loc.Shown(narKey);
            NarrationByDialog[Id(tree.TraderId, node.Name + "#nc" + s)] = loc.Shown(narKey);
            built.Add((Id(tree.TraderId, node.Name + "#nar" + s), new List<DialogLineTemplate> { Line(Id(tree.TraderId, node.Name + "#nl" + s), EDialogSide.Npc, DialogLineTemplate.EDialogLineIconType.DialogBubble, new DialogSwitchDialogAction(Id(tree.TraderId, node.Name + "#nc" + s)), narKey) }));
            built.Add((Id(tree.TraderId, node.Name + "#nc" + s), new List<DialogLineTemplate> { Line(Id(tree.TraderId, node.Name + "#ncl" + s), EDialogSide.Player, DialogLineTemplate.EDialogLineIconType.IndexFinger, new DialogSwitchDialogAction(next), loc.Put(tree.TraderId, node.Name, "cont" + s, Loc.Pick("继续…", "Continue..."), null)) }));
            Put(VoiceByDialog, Id(tree.TraderId, node.Name + "#nar" + s), node.Narration[s].Audio);
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

    internal static MongoID Id(string traderId, string name)
    {
        using var md5 = MD5.Create();
        var sb = new StringBuilder(24);
        foreach (var b in md5.ComputeHash(Encoding.UTF8.GetBytes(traderId + "|" + name))) { sb.Append(b.ToString("x2")); if (sb.Length >= 24) break; }
        return new MongoID(sb.ToString(0, 24));
    }
}
