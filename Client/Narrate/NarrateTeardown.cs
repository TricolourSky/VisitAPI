using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Comfort.Common;
using Diz.Jobs;
using EFT;
using EFT.CameraControl;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;
using UnityEngine;

namespace VisitAPI.Native;

[HarmonyPatch(typeof(TarkovApplication.NarrateController), "Hide")]
public static class NarrateHideGuard
{
    /// 09-24 审查 M2：这些清理以前直接排在 Prefix 里，任何一个抛异常，Harmony 就跳过原版 Hide（不切回主菜单、不关访问混音、不释放访问相机），
    /// 排在最后的 EnsureMenu 兜底也跑不到，访问后只能强退。现在每步单独兜住。
    /// 同时只在真有访问要收尾时才做：引擎把 HandleActionFinished 挂在常驻的菜单对话控制器上，那上面任何一个 Quit 都会调 Hide，
    /// 没有访问时去清「当前」贴花渲染器（那时可能是藏身处的）、复位战局标记是误伤。
    static void Prefix(TarkovApplication.NarrateController __instance)
    {
        if (__instance == null || (!__instance.GameExist && __instance._gameWorld == null))
        {
            return;
        }
        LocalSteps();
    }

    /// 访问收尾里我们自己的本地清理（Hide 前缀和 Show 半途失败的清理共用），每步单独兜住
    internal static void LocalSteps()
    {
        Step("lights", ForeignLights.Restore);
        Step("texture pin", TexturePin.Restore);
        Step("decal cache", DecalGuard.ClearVisit);
        Step("post chain", PostChain.Clear);
        Step("prism pin", () => PrismTransplant.Pinned = false);
        Step("tab router", TabRouter.UnwatchNarrate);
        Step("camera pin", VisitCameraPin.Clear);
        Step("raid flags", RaidFlagReset.Run);
        Step("menu", NarrateEntry.EnsureMenu);
    }

    /// 09-24 审查 M5：引擎 Show 半途失败、没建成 NarrateGame 时的清理。引擎在 try 外就开了 JobScheduler 强制模式、打开藏身处加载屏、
    /// 加载了访问场景；try 里失败还可能留下一个建了一半的访问世界（GameWorld 单例还指着它）。以前这里只做本地还原，这些都没人管
    internal static void CleanupFailedShow(TarkovApplication.NarrateController nc)
    {
        LocalSteps();
        Step("force mode", () => Singleton<JobScheduler>.Instance?.SetForceMode(false));
        Step("loading screen", () =>
        {
            var ui = MonoBehaviourSingleton<PreloaderUI>.Instantiated ? MonoBehaviourSingleton<PreloaderUI>.Instance : null;
            var screen = ui != null ? ui.HideoutLoadingScreen : null;
            if (screen != null && screen.gameObject.activeSelf) screen.Close();
        });
        if (nc != null && (nc._game != null || nc._gameWorld != null)) Step("orphan world", () => Teardown(nc));
        else Step("scenes", () => Plugin.Instance.StartCoroutine(UnloadScenes()));
    }

    static void Step(string name, Action act)
    {
        try { act(); }
        catch (Exception e) { Plugin.Log.LogError($"[narrate] Visit teardown step '{name}' failed (continuing with the rest, original Hide still runs): {e}"); }
    }

    static void Postfix(TarkovApplication.NarrateController __instance) => Teardown(__instance);

    /// 每次访问结束都把访问世界和 NarrateGame 拆掉（下次访问重新建，房间场景也卸掉）。
    /// 09-24 审查 H2：以前只 Stop + 手工释放单例和销毁世界，然后用新的 _unsubscriber 直接替换旧的——旧袋子从不 Dispose，
    /// 于是 GameWorld.Dispose 和 NarrateGame.Dispose（AbstractGame.Dispose：DestroyImmediate 那个 "GAME" 物体，连同它逐帧跑的协程）每次访问都漏掉；
    /// NarrateGame.Create 里建的任务 / 成就控制器引擎自己也从不释放。现在按原版 Unload 的做法整袋释放，并单独收掉那两个控制器。
    static void Teardown(TarkovApplication.NarrateController nc)
    {
        var game = nc._game;
        var world = nc._gameWorld;
        if (game == null && world == null) return;
        try { game?.Stop(); }
        catch (Exception ex) { Plugin.Log.LogWarning("[narrate] game stop failed: " + ex.Message); }
        // 1.3.4 B1：Stop 之后还开着的访问玩家是建到一半的孤儿（PlayerOwner 没赋值，原生 Stop 不管它）
        try { NarrateOrphans.Collect(); }
        catch (Exception ex) { Plugin.Log.LogWarning("[narrate] orphan player sweep failed: " + ex.Message); }
        VisitGameGuard.Release(game);
        // 袋子里依次是：world.Dispose → 「释放 GameWorld / IGameLevel 单例 + 销毁 _gameWorld」→ game.Dispose。
        // 第二项读的是 _gameWorld 字段，而上面 Stop 经 End 回调已经把它置空了——先放回去，否则那一项空引用，后面的 game.Dispose 也跟着不执行
        var bag = nc._unsubscriber;
        nc._unsubscriber = new CompositeDisposable();
        nc._gameWorld = world;
        var disposed = false;
        try { bag.Dispose(); disposed = true; }
        catch (Exception ex) { Plugin.Log.LogWarning("[narrate] Disposing the visit world bag failed, falling back to manual item-by-item teardown: " + ex); }
        // 不管袋子里有没有这个世界（Show 在 InitLevel 就失败时世界还没登记进袋子）都按实例补一遍：
        // TryRelease 只在单例还指着它时才清（Release 是无条件清空，可能清掉别的世界），重复 Destroy 无害
        if (world != null)
        {
            try
            {
                Singleton<GameWorld>.TryRelease(world);
                Singleton<IGameLevel>.TryRelease(world);
                UnityEngine.Object.Destroy(world.gameObject);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[narrate] Manual visit world teardown failed: " + ex.Message); }
        }
        if (!disposed)
        {
            // 袋子半途失败时 world.Dispose 可能没走到末尾的 DataProvider.Dispose、game.Dispose 也没执行
            if (world != null)
            {
                try { EFT.DataProviding.DataProvider.Instance.Dispose(); }
                catch (Exception ex2) { Plugin.Log.LogWarning("[narrate] data provider cleanup failed: " + ex2.Message); }
            }
            try { if (game != null) UnityEngine.Object.Destroy(game.gameObject); }
            catch (Exception ex) { Plugin.Log.LogWarning("[narrate] Failed to destroy NarrateGame object: " + ex.Message); }
        }
        nc._game = null;
        nc._gameWorld = null;
        Plugin.Instance.StartCoroutine(UnloadScenes());
    }

    /// 访问房间还在卸（B11 等告别动画的那几秒 + 卸载本身）：这期间不能开新的访问，否则 UnloadAll 会把新访问刚载的同名房间一起卸掉
    public static bool Unloading { get; private set; }

    static IEnumerator UnloadScenes()
    {
        Unloading = true;
        try
        {
            // 1.3.4 B11：告别动画播完再卸房间（相机已经拆了，这几秒房间在后台不可见）
            var t0 = Time.realtimeSinceStartup;
            while (NpcAnimations.Playing && Time.realtimeSinceStartup - t0 < NpcAnimations.MaxWait) yield return null;
            if (NpcAnimations.Playing) Plugin.Log.LogWarning($"[narrate] trader animation still playing after {NpcAnimations.MaxWait:0}s, unloading the room anyway");
            NpcAnimations.Forget();
            yield return UnloadNow();
        }
        finally { Unloading = false; }
    }

    static IEnumerator UnloadNow()
    {
        Task task = TarkovApplication.NarrateController.Scenes.UnloadAll();
        while (!task.IsCompleted) yield return null;
        AmbientGuard.Clear();
        try { DecalGuard.RebuildRemaining(); }
        catch (Exception e) { Plugin.Log.LogWarning("[narrate] Failed to rebuild static decal buffers after visit: " + e.Message); }
    }

    static Exception Finalizer(TarkovApplication.NarrateController __instance, Exception __exception, ref Task __result)
    {
        if (__exception == null) return null;
        Plugin.Log.LogWarning("[narrate] <<< controller.Hide faulted (swallowed): " + __exception.Message);
        try { Teardown(__instance); }
        catch (Exception ex) { Plugin.Log.LogError("[narrate] teardown after faulted Hide failed: " + ex); }
        __result ??= Task.CompletedTask;
        return null;
    }
}

[HarmonyPatch(typeof(NarrateGame), "Hide")]
public static class NarrateGameHideGuard
{
    static Exception Finalizer(NarrateGame __instance, Exception __exception)
    {
        if (__exception == null) return null;
        Plugin.Log.LogWarning("[narrate] game.Hide faulted (recovering): " + __exception.Message);
        var owner = __instance.PlayerOwner;
        if ((UnityEngine.Object)(object)owner != null)
        {
            try { owner.vmethod_1(); }
            catch (Exception ex) { Plugin.Log.LogWarning("[narrate] input release failed: " + ex.Message); }
            try { PlayerCameraController.Destroy(owner.Player); }
            catch (Exception ex2) { Plugin.Log.LogWarning("[narrate] camera teardown failed: " + ex2.Message); }
        }
        return null;
    }
}

/// <summary>09-24 审查 H2：访问用的 NarrateGame 在 Create 里另建一个 QuestControllerClientBackend 和一个 AchievementsControllerClientBackend 并 Run。
/// 我们每次访问都新建 NarrateGame，所以每次访问都多一对，而引擎从不释放它们（原版 Unload 只 Dispose 世界和 NarrateGame 本身）。
/// 它们的任务对象和主菜单那份共用档案里的任务数据和计数器：留着不放，每个旧副本都还挂在各商人的 OnSoldToTrader、背包事件、档案变量上，
/// 卖一次东西「出售给商人」类计数就被每个旧副本各加一遍，任务状态也会被旧副本各自再判一遍。这里：
///   ① 认出访问用的任务控制器（Create 期间构造的那个），章节链、LocalFail 跳过它，不让它把全局 ChapterChain.Controller 换成访问副本；
///   ② 不让它重发「失败可重开任务」的 QuestAccept（Run 里 RestartFailedRestartableQuests，主菜单的控制器登录时已经发过）；
///   ③ 访问收尾时 Dispose 这两个控制器——和原版进战局时释放主菜单那一对（MainMenuShowOperation 收尾）是同一条路，
///      ConditionalController.Dispose 会退掉 OnSoldToTrader（订阅和退订绑的是同一个闭包方法，IL 核过）和其余订阅。</summary>
public static class VisitGameGuard
{
    static readonly FieldInfo QuestField = AccessTools.Field(typeof(NarrateGame), "questControllerClientBackend");
    static readonly FieldInfo AchievementsField = AccessTools.Field(typeof(NarrateGame), "achievementsControllerClientBackend");
    static readonly HashSet<QuestController> _controllers = new();
    static bool _creating;

    public static bool IsVisitController(QuestController qc) => qc != null && _controllers.Contains(qc);

    [HarmonyPatch(typeof(NarrateGame), nameof(NarrateGame.Create))]
    public static class Create
    {
        static void Prefix() => _creating = true;
        static Exception Finalizer(Exception __exception) { _creating = false; return __exception; }
    }

    [HarmonyPatch(typeof(QuestControllerClientBackend), MethodType.Constructor, typeof(Profile), typeof(EFT.InventoryLogic.InventoryController), typeof(IQuestSession))]
    public static class Construct
    {
        static void Postfix(QuestControllerClientBackend __instance)
        {
            if (!_creating) return;
            _controllers.Add(__instance);
        }
    }

    /// 原版 NarrateController.Unload（进战局、进藏身处等 6 处）走的是 _unsubscriber → game.Dispose，不经过我们的 Teardown，这里也收一次。
    /// 用 Finalizer：Dispose 里先 Stop（玩家 OnGameSessionEnd 还要用控制器），完了再释放；Teardown 已经释放过的话字段是空的，什么也不做
    [HarmonyPatch(typeof(NarrateGame), nameof(NarrateGame.Dispose))]
    public static class Disposed
    {
        static Exception Finalizer(NarrateGame __instance, Exception __exception)
        {
            try { Release(__instance); }
            catch (Exception e) { Plugin.Log.LogWarning("[narrate] Failed to release visit controllers after NarrateGame.Dispose: " + e.Message); }
            // 09-26：原生 Unload 这条路不经过 Hide（没走 LocalSteps），相机挂点也在这里还原，免得回池的访问玩家带着偏移被藏身处复用
            try { VisitCameraPin.Clear(); }
            catch (Exception e) { Plugin.Log.LogWarning("[narrate] Failed to restore the camera container after NarrateGame.Dispose: " + e.Message); }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(QuestControllerClientBackend), nameof(QuestControllerClientBackend.RestartFailedRestartableQuests))]
    public static class NoRestart
    {
        static bool Prefix(QuestControllerClientBackend __instance, ref Task __result)
        {
            if (!IsVisitController(__instance)) return true;
            __result = Task.CompletedTask;
            return false;
        }
    }

    /// 访问收尾：释放 NarrateGame 的两个控制器。要在 game.Stop() 之后调——Stop 里玩家的 OnGameSessionEnd 还会用到它们
    public static void Release(NarrateGame game)
    {
        // 用引用判空：AbstractGame.Dispose 末尾 DestroyImmediate 了自己的物体，Disposed 的 Finalizer 里 game == null 已经是 true（Unity 的假空），
        // 但托管字段还在，照样能取出控制器释放
        if (ReferenceEquals(game, null)) return;
        foreach (var field in new[] { QuestField, AchievementsField })
        {
            if (field == null) continue;
            var controller = field.GetValue(game);
            if (controller == null) continue;
            if (controller is QuestController qc) _controllers.Remove(qc);
            try { (controller as IDisposable)?.Dispose(); }
            catch (Exception e) { Plugin.Log.LogWarning($"[narrate] Failed to release the visit's {controller.GetType().Name}: {e.Message}"); }
            try { field.SetValue(game, null); } catch { }
        }
    }
}
