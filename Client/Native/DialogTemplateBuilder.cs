using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using EFT;
using EFT.AnimationSequencePlayer;
using EFT.Dialogs;
using EFT.Quests;
using VisitAPI.Dialog;

namespace VisitAPI.Native;

public static class DialogTemplateBuilder
{
    public static readonly Dictionary<MongoID, string> BgByDialog = new();
    public static readonly Dictionary<MongoID, string> VoiceByDialog = new();
    public static readonly Dictionary<MongoID, string> BgmByDialog = new();
    public static readonly Dictionary<MongoID, string> NodeByDialog = new();

    /// <summary>旁白拍（#nc，就是那条"继续…"玩家行）→ 要写进原生字幕框的文字。见 NarrationView。</summary>
    public static readonly Dictionary<MongoID, string> NarrationByDialog = new();

    public static MongoID Register(DialogTree tree, string entryNode, string startNode, string playerName, string profileId, QuestController quests)
    {
        WhitelistPatch.RegisteredTraders.Add(tree.TraderId);
        var loc = new Dictionary<string, string>();
        var built = new List<(MongoID id, List<DialogLineTemplate> lines)>();
        foreach (var node in tree.Nodes.Values)
        {
            // 一屏的播放顺序：旁白 [0, NpcSlot) → 台词 → 旁白 [NpcSlot, 末尾)。
            // slots 里 -1 代表台词那一拍，其余是 Narration 的下标 —— 对话 id 仍按下标起名，
            // 作者换个顺序不会让 id 满天飞（存档里的 once 记号是按 id 存的）。
            var slots = new List<int>();
            for (var i = 0; i < node.NpcSlot; i++) slots.Add(i);
            slots.Add(-1);
            for (var i = node.NpcSlot; i < node.Narration.Count; i++) slots.Add(i);
            NodeByDialog[Id(tree.TraderId, node.Name + "#npc")] = node.Name;
            NodeByDialog[Id(tree.TraderId, node.Name + "#opt")] = node.Name;
            Put(VoiceByDialog, Id(tree.TraderId, node.Name + "#npc"), node.NpcAudio);
            // 节点级的背景和 BGM 挂在**真正的第一拍**上，那一拍不一定还是旁白
            Put(BgmByDialog, SlotId(tree, node, slots[0]), node.Bgm);
            var after = node.JumpTo != null && tree.Nodes.ContainsKey(node.JumpTo) ? OptionMap.Entry(tree, node.JumpTo) : Id(tree.TraderId, node.Name + "#opt");
            for (var p = 0; p < slots.Count; p++)
            {
                var s = slots[p];
                var next = p + 1 < slots.Count ? SlotId(tree, node, slots[p + 1]) : after;
                Put(BgByDialog, SlotId(tree, node, s), p == 0 ? (s < 0 ? node.Bg : node.Narration[s].Bg ?? node.Bg) : (s < 0 ? null : node.Narration[s].Bg));
                if (s < 0)
                {
                    var say = Line(Id(tree.TraderId, node.Name + "#say"), EDialogSide.Npc, DialogLineTemplate.EDialogLineIconType.DialogBubble,
                        new DialogSwitchDialogAction(next), Key(loc, tree.TraderId, node.Name, "npc", node.NpcText ?? "……", playerName));
                    built.Add((Id(tree.TraderId, node.Name + "#npc"), new List<DialogLineTemplate> { say }));
                    continue;
                }
                var narKey = Key(loc, tree.TraderId, node.Name, "nar" + s, node.Narration[s].Text, playerName);
                // NPC 拍(#nar)和玩家拍(#nc)都登记：引擎过 #nar 时是一次异步网络往返，
                // 只登记 #nc 的话那段时间字幕会掉、商人对话窗会闪出来
                NarrationByDialog[Id(tree.TraderId, node.Name + "#nar" + s)] = loc[narKey];
                NarrationByDialog[Id(tree.TraderId, node.Name + "#nc" + s)] = loc[narKey];
                built.Add((Id(tree.TraderId, node.Name + "#nar" + s), new List<DialogLineTemplate> { Line(Id(tree.TraderId, node.Name + "#nl" + s), EDialogSide.Npc, DialogLineTemplate.EDialogLineIconType.DialogBubble, new DialogSwitchDialogAction(Id(tree.TraderId, node.Name + "#nc" + s)), narKey) }));
                built.Add((Id(tree.TraderId, node.Name + "#nc" + s), new List<DialogLineTemplate> { Line(Id(tree.TraderId, node.Name + "#ncl" + s), EDialogSide.Player, DialogLineTemplate.EDialogLineIconType.IndexFinger, new DialogSwitchDialogAction(next), Key(loc, tree.TraderId, node.Name, "cont" + s, Loc.Pick("继续…", "Continue..."), playerName)) }));
                Put(VoiceByDialog, Id(tree.TraderId, node.Name + "#nar" + s), node.Narration[s].Audio);
            }
            var rows = new List<DialogLineTemplate>();
            for (var i = 0; i < node.Options.Count; i++)
            {
                var o = node.Options[i];
                var lineId = Id(tree.TraderId, node.Name + "#" + i);
                if (o.StandingDelta != 0) StandingService.Register(lineId, o.StandingTraderId ?? tree.TraderId, o.StandingDelta);
                if (o.SetStatusId != null) SetStatusService.Register(lineId, o.SetStatusId, o.SetStatusValue);
                if (o.HandoverId != null) HandoverService.Register(lineId, o.HandoverId);
                if (o.Target == "@trade") TabRouter.Register(lineId, EFT.UI.TraderScreensGroup.ETraderMode.Trade);
                else if (o.Target == "@tasks") TabRouter.Register(lineId, EFT.UI.TraderScreensGroup.ETraderMode.Tasks);
                else if (o.Target == "@services") TabRouter.Register(lineId, EFT.UI.TraderScreensGroup.ETraderMode.Services);
                var once = o.Once ? OnceService.Register(lineId, tree.TraderId, profileId, node.Name, i) : null;
                rows.Add(Line(lineId, EDialogSide.Player, OptionMap.Icon(o), QuestGates.Actions(o, OptionMap.Act(tree, startNode, o.Target)), QuestGates.Trigger(o, quests, once), Key(loc, tree.TraderId, node.Name, "opt" + i, o.Text, playerName)));
            }
            if (rows.Count == 0 && node.JumpTo == null)
                rows.Add(Line(Id(tree.TraderId, node.Name + "#end"), EDialogSide.Player, DialogLineTemplate.EDialogLineIconType.QuitIcon, new DialogQuitAction(), Key(loc, tree.TraderId, node.Name, "end", Loc.Pick("（结束）", "(End)"), playerName)));
            built.Add((Id(tree.TraderId, node.Name + "#opt"), rows));
        }
        var locByLang = new Dictionary<string, Dictionary<string, string>> { ["ch"] = loc, ["en"] = loc };
        foreach (var (id, lines) in built)
            DialogStorage.Instance.AddTemplate(new TraderDialogTemplate(id, new MongoID(tree.TraderId), new MongoID[0], lines, locByLang) { CanBeFirstDialog = true });
        return OptionMap.Entry(tree, entryNode);
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

    static string Key(Dictionary<string, string> loc, string trader, string node, string tag, string text, string nick)
    {
        var k = $"visitapi_{trader}_{node}_{tag}";
        loc[k] = text.Replace("{playerName}", nick).Replace("{player}", nick);
        return k;
    }

    /// <summary>一拍的对话 id。s &lt; 0 是台词那一拍，其余是 Narration 的下标。</summary>
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
