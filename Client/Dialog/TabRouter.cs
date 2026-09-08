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

/// <summary>「对话里开商人页」的唯一实现（Refactor_Plan：TabRouter 吸收 NarrateTabs，两套近似流程收成一条尾巴）。
/// 自定义路径：`@trade/@tasks/@services` 选项 → 关对话开商人页 → 关闭后回到原对话节点；
/// 原生路径：访问对话里点「任务」（DialogQuestsScreenAction）→ 商人任务页。单商人假设：同时只有一场访问。</summary>
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
        DialogWindowOpen = true;
        yield return ShowAt(sc, mode);
    }

    /// 共用尾巴：亮出备好的控制器，等商人屏就位，切到指定页（Trade 是默认页不用切）
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

    // ── 原生访问路径（原 NarrateTabs，2026-09-02 阶段四并入）──

    static BaseTraderDialogController _watched;
    static string _watchedTraderId;

    public static void WatchNarrate(TarkovApplication app, string traderId)
    {
        _watchedTraderId = traderId;
        if (!(NarrateEntry.MenuOpField?.GetValue(app) is MainMenuShowOperation op) || op.DialogController == null) return;
        if (_watched != null) _watched.OnActionFinished -= HandleNarrate;
        _watched = op.DialogController;
        _watched.OnActionFinished -= HandleNarrate;   // 防同一 controller 重复订阅
        _watched.OnActionFinished += HandleNarrate;
    }

    /// 退出访问时退订（NarrateHideGuard.Prefix 调）：订阅留着的话之后任何对话里的 DialogQuestsScreenAction 都会开**上一位**商人的任务页
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
