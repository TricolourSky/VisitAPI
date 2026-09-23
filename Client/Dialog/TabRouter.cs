using System.Collections;
using System.Linq;
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
    public static bool DialogWindowOpen;

    internal static IEnumerator OpenTradeWindow(DialogTree tree, string node, TraderScreensGroup screen, Profile profile, QuestController quests, InventoryController inventory, TraderScreensGroup.ETraderMode mode)
    {
        yield return UiWait.Until(() => !DialogScreenTracker.Open, 180);
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
        if (Tsg == null) { Plugin.Log.LogWarning("[tab] no trader screen group - cannot open trader screen"); DialogBackground.Discard(); yield break; }
        DialogWindowOpen = true;
        yield return ShowAt(sc, mode);
        if (!Active) { Plugin.Log.LogWarning("[tab] trader screen did not show, releasing dialog-window flag"); DialogWindowOpen = false; DialogBackground.Discard(); }
    }

    public static bool Active => DialogWindowOpen && Tsg != null && Tsg.isActiveAndEnabled;

    static TraderScreensGroup Tsg => MonoBehaviourSingleton<MenuUI>.Instance != null ? MonoBehaviourSingleton<MenuUI>.Instance.TraderScreensGroup : null;

    static IEnumerator ShowAt(TraderScreensGroup.TraderScreenController sc, TraderScreensGroup.ETraderMode mode)
    {
        sc.ShowScreen(EScreenState.Queued);
        var tsg = Tsg;
        if (tsg == null) yield break;
        yield return UiWait.Until(() => tsg.isActiveAndEnabled, 60);
        yield return null;
        if (tsg.isActiveAndEnabled && mode != TraderScreensGroup.ETraderMode.Trade) tsg.SetMode(mode);
    }

    static IEnumerator ReopenDialog(DialogTree tree, string node, TraderScreensGroup screen, Profile profile, QuestController quests, InventoryController inventory)
    {
        var tsg = Tsg;
        yield return UiWait.Until(() => tsg != null && tsg.isActiveAndEnabled, 30);
        yield return null;
        if (!DialogOpener.TryOpenAt(tree, node, profile, quests, inventory, screen, out var err))
        { Plugin.Log.LogWarning("[tab] dialog reopen failed: " + err); DialogBackground.Discard(); }
    }

    static BaseTraderDialogController _watched;
    static string _watchedTraderId;

    public static void WatchNarrate(TarkovApplication app, string traderId)
    {
        _watchedTraderId = traderId;
        if (!(NarrateEntry.MenuOpField?.GetValue(app) is MainMenuShowOperation op) || op.DialogController == null) return;
        if (_watched != null) _watched.OnActionFinished -= HandleNarrate;
        _watched = op.DialogController;
        _watched.OnActionFinished -= HandleNarrate;
        _watched.OnActionFinished += HandleNarrate;
    }

    public static void UnwatchNarrate()
    {
        if (_watched != null) _watched.OnActionFinished -= HandleNarrate;
        _watched = null;
        _watchedTraderId = null;
    }

    static void HandleNarrate(DialogAction action)
    {
        if (action is DialogQuestsScreenAction) Plugin.Instance.StartCoroutine(OpenNarrateTasks(_watchedTraderId));
    }

    static IEnumerator OpenNarrateTasks(string traderId)
    {
        yield return null;
        if (traderId == null || !TarkovApplication.Exist(out var app) || app.Session == null || !(NarrateEntry.MenuOpField?.GetValue(app) is MainMenuShowOperation op)) yield break;
        var trader = app.Session.Traders.FirstOrDefault(t => t.Id == traderId);
        if (trader == null) { Plugin.Log.LogWarning("[narrate] tasks: trader not in session " + traderId); yield break; }
        var sc = new TraderScreensGroup.TraderScreenController(trader, new[] { trader }, app.Session.Profile, op.InventoryController,
            op.HealthController, op.QuestController, op.achievementsController, app.Session);
        yield return ShowAt(sc, TraderScreensGroup.ETraderMode.Tasks);
        Plugin.Log.LogDebug("[narrate] tasks screen opened for " + traderId);
    }
}
