using System.Collections;
using System.Linq;
using EFT;
using EFT.Dialogs;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.UI;
using EFT.UI.Screens;
using VisitAPI.Dialog;

namespace VisitAPI.Native;

public static class DialogOpener
{
    public static bool TryOpenTriggered(DialogTree tree, string node, out string error)
    {
        var player = GamePlayerOwner.MyPlayer;
        var first = player != null && node == null && tree.First != null && tree.Nodes.ContainsKey(tree.First) && !OnceService.Used(player.Profile, OnceService.FirstId(tree.TraderId));
        if (first) node = tree.First;
        var ok = TryOpenPlayer(tree, node, out error);
        if (ok && first) OnceService.Mark(player.Profile, OnceService.FirstId(tree.TraderId), "first");
        return ok;
    }

    static bool TryOpenPlayer(DialogTree tree, string forceNode, out string error)
    {
        var player = GamePlayerOwner.MyPlayer;
        if (player == null) { error = "no player entity - open the dialog INSIDE the hideout or a raid"; return false; }
        var ok = TryOpen(tree, player.Profile, QuestOps.Resolve(), player.InventoryController, null, forceNode, false, out error);
        if (ok) InputGuard.Block();
        return ok;
    }

    public static bool TryOpen(DialogTree tree, Profile profile, QuestController quests, InventoryController inventory, TraderScreensGroup tradeScreen, out string error) =>
        TryOpen(tree, profile, quests, inventory, tradeScreen, null, true, out error);

    public static bool TryOpenAt(DialogTree tree, string node, Profile profile, QuestController quests, InventoryController inventory, TraderScreensGroup tradeScreen, out string error) =>
        TryOpen(tree, profile, quests, inventory, tradeScreen, node, false, out error, atOptions: true);

    static bool TryOpen(DialogTree tree, Profile profile, QuestController quests, InventoryController inventory, TraderScreensGroup tradeScreen, string forceNode, bool scene, out string error, bool atOptions = false)
    {
        error = null;
        if (!profile.TradersInfo.ContainsKey(new MongoID(tree.TraderId))) { error = $"trader {tree.TraderId} not found in profile"; return false; }
        OnceService.Migrate(profile);
        var startNode = ResolveStart(tree, profile);
        var entryNode = forceNode ?? startNode;
        if (!tree.Nodes.ContainsKey(entryNode)) { error = $"start node '{entryNode}' not found in .dlg"; return false; }
        var entry = DialogTemplateBuilder.Register(tree, entryNode, startNode, profile.Nickname, quests);
        if (atOptions) entry = DialogTemplateBuilder.Id(tree.TraderId, entryNode + "#opt");
        var dc = new ClientDialogController(profile, quests, inventory);
        new TraderDialogScreen.TraderDialogScreenController(profile, tree.TraderId, quests,
            inventory, null, dc, entry).ShowScreen(EScreenState.Queued);
        RetailDialogs.SeedVariables(dc);
        DialogSession.Begin(dc, tree, profile, quests, inventory, tradeScreen);
        if (scene && tree.Scene != null && !Raid.Now)
            SceneLoader.Open(tree.Scene == "auto" ? tree.TraderId : tree.Scene, dc);
        DialogBackground.Attach(dc);
        if (atOptions) Plugin.Instance.StartCoroutine(SeedNpcLine(dc, tree.TraderId, entryNode));
        return true;
    }

    static IEnumerator SeedNpcLine(ClientDialogController dc, string traderId, string node)
    {
        yield return UiWait.Until(() => dc.CurrentDialog != null, 300);
        if (dc.CurrentDialog == null) yield break;
        var say = dc.method_0(DialogTemplateBuilder.Id(traderId, node + "#npc"))?.Lines?.FirstOrDefault();
        if (say == null) yield break;
        dc.LastNpcLine = say;
        dc.History.AddLine(say);
        dc.SetCurrentDialog(dc.method_0(dc.CurrentDialog.Id));
    }

    static string ResolveStart(DialogTree tree, Profile profile)
    {
        double level = profile.Info.Level;
        double standing = profile.TradersInfo[new MongoID(tree.TraderId)].Standing;
        var pick = tree.Start;
        foreach (var rule in tree.WhenRules)
        {
            var ok = rule.Conds.TrueForAll(c => { var v = c.Field == "level" ? level : standing; return c.LessEq ? v <= c.Value : v >= c.Value; });
            if (ok && tree.Nodes.ContainsKey(rule.Node)) { pick = rule.Node; break; }
        }
        Plugin.Log.LogDebug($"[visit] start={pick} (level={level} standing={standing} whenRules={tree.WhenRules.Count})");
        return pick;
    }
}
