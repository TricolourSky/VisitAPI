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

/// <summary>
/// 把 `DialogTree` 编译成引擎的 `TraderDialogTemplate`/`DialogLineTemplate`。
/// ⚠️ 对话 id 生成算法（MD5 前 24 位）与拍子/命名规则是**存档兼容契约**：
/// `.seen.json` 的 once/first 记号按这些 id 存，改了算法旧存档记号全部失效。1.2.1 起冻结。
/// </summary>
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
            Beats(tree, node, playerName, loc, built);
            var rows = new List<DialogLineTemplate>();
            var open = false;   // 这一屏有没有一条"不带任何门控"的行
            for (var i = 0; i < node.Options.Count; i++)
            {
                var o = node.Options[i];
                var lineId = Id(tree.TraderId, node.Name + "#" + i);
                var e = Effect(tree, node, o, i, lineId, profileId);
                var once = o.Once ? new OnceGate(OnceService.Store(tree.TraderId), profileId, node.Name, i) : null;
                var trigger = QuestGates.Trigger(o, quests, once);
                open |= trigger == null;
                var main = OptionMap.Act(tree, startNode, o.Target);
                if (node.NpcSlot < node.Narration.Count) main = new DialogSwitchDialogAction(Epilogue(tree, node, i, main, e, loc, built, playerName));   // 选完先播收尾旁白
                rows.Add(Line(lineId, EDialogSide.Player, OptionMap.Icon(o), QuestGates.Actions(o, main), trigger, Key(loc, tree.TraderId, node.Name, "opt" + i, o.Text, playerName)));
            }
            // 死锁保护：一屏的选项**全被门控挡掉**时（不只是一条都没写），补一条无条件的「（结束）」。
            // 不补的话引擎会自己生成一条红色 "Back"（DynamicTraderDialog.CheckEmptyLines），
            // 那条路上 TradersSettings 是裸索引、自定义商人还有抛异常的风险；而战局内对话不能裸 ESC 退出。
            // 作者留了 `always` 出口的节点 open 为真，一个字都不会多出来。旧 DEV_NOTES #80。
            if (!open && node.JumpTo == null)
                rows.Add(Line(Id(tree.TraderId, node.Name + "#end"), EDialogSide.Player, DialogLineTemplate.EDialogLineIconType.QuitIcon, new DialogQuitAction(), Key(loc, tree.TraderId, node.Name, "end", Loc.Pick("（结束）", "(End)"), playerName)));
            built.Add((Id(tree.TraderId, node.Name + "#opt"), rows));
        }
        var locByLang = new Dictionary<string, Dictionary<string, string>> { ["ch"] = loc, ["en"] = loc };
        foreach (var (id, lines) in built)
            DialogStorage.Instance.AddTemplate(new TraderDialogTemplate(id, new MongoID(tree.TraderId), new MongoID[0], lines, locByLang) { CanBeFirstDialog = true });
        return OptionMap.Entry(tree, entryNode);
    }

    /// 一条选项的副作用记录（DialogSession 按行 id 分发）。全字段赋值：重开对话重新编译时旧值整条覆盖
    static LineEffect Effect(DialogTree tree, DialogNode node, DialogOption o, int i, MongoID lineId, string profileId)
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
        e.Once = o.Once ? (tree.TraderId, profileId, node.Name, i) : ((string, string, string, int)?)null;
        return e;
    }

    /// 一个节点的"拍子"：旁白 / 台词 / 「继续…」按 NpcSlot 排好序，各自登记成对话模板
    static void Beats(DialogTree tree, DialogNode node, string playerName, Dictionary<string, string> loc, List<(MongoID id, List<DialogLineTemplate> lines)> built)
    {
        // 一屏的播放顺序：旁白 [0, NpcSlot) → 台词 → 旁白 [NpcSlot, 末尾)。
        // slots 里 -1 代表台词那一拍，其余是 Narration 的下标 —— 对话 id 仍按下标起名，
        // 作者换个顺序不会让 id 满天飞（存档里的 once 记号是按 id 存的）。
        // 台词后面的旁白不进这里的拍子序列——它们是选完选项之后的收尾（Epilogue）
        var slots = new List<int>();
        for (var i = 0; i < node.NpcSlot; i++) slots.Add(i);
        slots.Add(-1);
        NodeByDialog[Id(tree.TraderId, node.Name + "#npc")] = node.Name;
        NodeByDialog[Id(tree.TraderId, node.Name + "#opt")] = node.Name;
        Put(VoiceByDialog, Id(tree.TraderId, node.Name + "#npc"), node.NpcAudio);
        // 节点级的背景和 BGM 挂在**真正的第一拍**上，那一拍不一定还是旁白
        Put(BgmByDialog, SlotId(tree, node, slots[0]), node.Bgm);
        var jump = node.JumpTo != null && tree.Nodes.ContainsKey(node.JumpTo) ? OptionMap.Entry(tree, node.JumpTo) : (MongoID?)null;
        var after = jump == null ? Id(tree.TraderId, node.Name + "#opt")
                  : node.NpcSlot < node.Narration.Count ? Epilogue(tree, node, 0, new DialogSwitchDialogAction(jump.Value), null, loc, built, playerName) : jump.Value;
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
    }

    /// <summary>台词后面的旁白（编辑器拍子条里排在「对话」右边的那些）= **选完选项之后的收尾**（SORA 的设计，坑 #102）：
    /// 选项 → 逐条字幕旁白 → 原来的去向（关闭 / 跳节点 / 开商人页）。每个选项各一条拍子链——去向不同没法共用；
    /// 纯旁白节点（`->`）也走这条，去向就是那个跳转。@trade 一类的开页效果挪到链尾那一拍，等旁白播完对话关了再开页。返回链头的对话 id。</summary>
    static MongoID Epilogue(DialogTree tree, DialogNode node, int option, DialogAction final, LineEffect effect, Dictionary<string, string> loc, List<(MongoID id, List<DialogLineTemplate> lines)> built, string playerName)
    {
        MongoID BeatId(int s) => Id(tree.TraderId, $"{node.Name}#e{option}_{s}");
        for (var s = node.NpcSlot; s < node.Narration.Count; s++)
        {
            var last = s == node.Narration.Count - 1;
            var id = BeatId(s);
            var lineId = Id(tree.TraderId, $"{node.Name}#el{option}_{s}");
            NodeByDialog[id] = node.Name;
            NarrationByDialog[id] = loc[Key(loc, tree.TraderId, node.Name, $"e{option}_{s}", node.Narration[s].Text, playerName)];
            Put(BgByDialog, id, node.Narration[s].Bg);
            Put(VoiceByDialog, id, node.Narration[s].Audio);
            built.Add((id, new List<DialogLineTemplate> { Line(lineId, EDialogSide.Player, DialogLineTemplate.EDialogLineIconType.IndexFinger, last ? final : new DialogSwitchDialogAction(BeatId(s + 1)), Key(loc, tree.TraderId, node.Name, $"econt{option}_{s}", Loc.Pick("继续…", "Continue..."), playerName)) }));
            LineEffects.For(lineId).Tab = last && effect != null ? effect.Tab : null;   // 全字段赋值：重开对话旧值整条覆盖
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

    static string Key(Dictionary<string, string> loc, string trader, string node, string tag, string text, string nick)
    {
        var k = $"visitapi_{trader}_{node}_{tag}";
        loc[k] = text.Replace("{playerName}", nick).Replace("{player}", nick);
        return k;
    }

    /// <summary>一拍的对话 id。s &lt; 0 是台词那一拍，其余是 Narration 的下标（台词前的那些；台词后的走 Epilogue）。</summary>
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
