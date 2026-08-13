using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Dialogs;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.UI;
using EFT.UI.Screens;
using VisitAPI.Dialog;

namespace VisitAPI.Native;

public static class TabRouter
{
    static readonly Dictionary<MongoID, TraderScreensGroup.ETraderMode> _byLine = new();
    public static bool DialogWindowOpen;

    public static void Register(MongoID lineId, TraderScreensGroup.ETraderMode mode) => _byLine[lineId] = mode;

    public static void Watch(BaseTraderDialogController dc, TraderScreensGroup screen, DialogTree tree, Profile profile, QuestController quests, InventoryController inventory)
    {
        dc.OnDialogChanged += dialog =>
        {
            if (dialog == null) return;
            dialog.OnExecuteLine += line =>
            {
                if (line?.Template == null || !_byLine.TryGetValue(line.Template.Id, out var mode)) return;
                if (Singleton<AbstractGame>.Instantiated && Singleton<AbstractGame>.Instance.InRaid) return;
                var node = dc.CurrentDialog != null && DialogTemplateBuilder.NodeByDialog.TryGetValue(dc.CurrentDialog.Id, out var n) ? n : null;
                DialogBackground.KeepAlive = true;
                Plugin.Instance.StartCoroutine(OpenTradeWindow(tree, node, screen, profile, quests, inventory, mode));
            };
        };
    }

    static IEnumerator OpenTradeWindow(DialogTree tree, string node, TraderScreensGroup screen, Profile profile, QuestController quests, InventoryController inventory, TraderScreensGroup.ETraderMode mode)
    {
        for (var i = 0; i < 180 && UnityEngine.Object.FindObjectOfType<TraderDialogScreen>() != null; i++) yield return null;
        yield return null;
        yield return null;
        if (!TarkovApplication.Exist(out var app) || app.Session == null) { Plugin.Log.LogWarning("[tab] no session - cannot open trader screen"); DialogBackground.Discard(); yield break; }
        var session = app.Session;
        var trader = session.Traders.FirstOrDefault(t => t.Id == tree.TraderId);
        if (trader == null) { Plugin.Log.LogWarning("[tab] trader not in session: " + tree.TraderId); DialogBackground.Discard(); yield break; }
        var health = new OfflineHealthController(profile.Health, inventory, profile.Skills);
        var achievements = new AchievementsControllerClientBackend(profile, inventory, quests.Quests, session);
        var sc = new TraderScreensGroup.DialogTraderScreenController(trader, new[] { trader }, profile, inventory, health, quests, achievements, session);
        sc.OnClose += () => { DialogWindowOpen = false; DialogBackground.Cover(); Plugin.Instance.StartCoroutine(ReopenDialog(tree, node, screen, profile, quests, inventory)); };
        DialogWindowOpen = true;
        sc.ShowScreen(EScreenState.Queued);
        var tsg = MonoBehaviourSingleton<MenuUI>.Instance != null ? MonoBehaviourSingleton<MenuUI>.Instance.TraderScreensGroup : null;
        if (tsg != null) yield return ApplyWindow(tsg, mode);
    }

    static IEnumerator ApplyWindow(TraderScreensGroup tsg, TraderScreensGroup.ETraderMode mode)
    {
        for (var i = 0; i < 60 && !tsg.isActiveAndEnabled; i++) yield return null;
        yield return null;
        if (tsg.isActiveAndEnabled && mode != TraderScreensGroup.ETraderMode.Trade) tsg.SetMode(mode);
    }

    static IEnumerator ReopenDialog(DialogTree tree, string node, TraderScreensGroup screen, Profile profile, QuestController quests, InventoryController inventory)
    {
        var tsg = MonoBehaviourSingleton<MenuUI>.Instance != null ? MonoBehaviourSingleton<MenuUI>.Instance.TraderScreensGroup : null;
        for (var i = 0; i < 30 && !(tsg != null && tsg.isActiveAndEnabled); i++) yield return null;
        yield return null;
        if (!DialogOpener.TryOpenAt(tree, node, profile, quests, inventory, screen, out var err))
        { Plugin.Log.LogWarning("[tab] dialog reopen failed: " + err); DialogBackground.Discard(); }
    }
}
