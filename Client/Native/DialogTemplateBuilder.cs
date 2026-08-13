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

    public static MongoID Register(DialogTree tree, string entryNode, string startNode, string playerName, string profileId, QuestController quests)
    {
        WhitelistPatch.RegisteredTraders.Add(tree.TraderId);
        var loc = new Dictionary<string, string>();
        var built = new List<(MongoID id, List<DialogLineTemplate> lines)>();
        foreach (var node in tree.Nodes.Values)
        {
            for (var i = 0; i < node.Narration.Count; i++)
            {
                var next = i + 1 < node.Narration.Count ? Id(tree.TraderId, node.Name + "#nar" + (i + 1)) : Id(tree.TraderId, node.Name + "#npc");
                built.Add((Id(tree.TraderId, node.Name + "#nar" + i), new List<DialogLineTemplate> { Line(Id(tree.TraderId, node.Name + "#nl" + i), EDialogSide.Npc, DialogLineTemplate.EDialogLineIconType.DialogBubble, new DialogSwitchDialogAction(Id(tree.TraderId, node.Name + "#nc" + i)), Key(loc, tree.TraderId, node.Name, "nar" + i, node.Narration[i].Text, playerName)) }));
                built.Add((Id(tree.TraderId, node.Name + "#nc" + i), new List<DialogLineTemplate> { Line(Id(tree.TraderId, node.Name + "#ncl" + i), EDialogSide.Player, DialogLineTemplate.EDialogLineIconType.IndexFinger, new DialogSwitchDialogAction(next), Key(loc, tree.TraderId, node.Name, "cont" + i, Loc.Pick("继续…", "Continue..."), playerName)) }));
                Put(BgByDialog, Id(tree.TraderId, node.Name + "#nar" + i), i == 0 ? node.Narration[0].Bg ?? node.Bg : node.Narration[i].Bg);
                Put(VoiceByDialog, Id(tree.TraderId, node.Name + "#nar" + i), node.Narration[i].Audio);
            }
            NodeByDialog[Id(tree.TraderId, node.Name + "#npc")] = node.Name;
            NodeByDialog[Id(tree.TraderId, node.Name + "#opt")] = node.Name;
            Put(BgByDialog, Id(tree.TraderId, node.Name + "#npc"), node.Narration.Count == 0 ? node.Bg : null);
            Put(VoiceByDialog, Id(tree.TraderId, node.Name + "#npc"), node.NpcAudio);
            Put(BgmByDialog, Id(tree.TraderId, node.Name + (node.Narration.Count > 0 ? "#nar0" : "#npc")), node.Bgm);
            var after = node.JumpTo != null && tree.Nodes.ContainsKey(node.JumpTo) ? OptionMap.Entry(tree, node.JumpTo) : Id(tree.TraderId, node.Name + "#opt");
            var say = Line(Id(tree.TraderId, node.Name + "#say"), EDialogSide.Npc, DialogLineTemplate.EDialogLineIconType.DialogBubble,
                new DialogSwitchDialogAction(after), Key(loc, tree.TraderId, node.Name, "npc", node.NpcText ?? "……", playerName));
            built.Add((Id(tree.TraderId, node.Name + "#npc"), new List<DialogLineTemplate> { say }));
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

    internal static MongoID Id(string traderId, string name)
    {
        using var md5 = MD5.Create();
        var sb = new StringBuilder(24);
        foreach (var b in md5.ComputeHash(Encoding.UTF8.GetBytes(traderId + "|" + name))) { sb.Append(b.ToString("x2")); if (sb.Length >= 24) break; }
        return new MongoID(sb.ToString(0, 24));
    }
}
