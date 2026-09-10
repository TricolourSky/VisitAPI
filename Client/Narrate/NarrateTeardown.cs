using System;
using System.Collections;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using HarmonyLib;

namespace VisitAPI.Native;

// ══ 退出访问的两层兜底（同一份退出，各兜各的层）══
// NarrateHideGuard  管 controller 层：清贴花缓存 → 恢复菜单 → 原方法 → 手动释放残留单例/场景 → 吞异常。
// NarrateGameHideGuard 管 game 层：NarrateGame.Hide 自己抛异常时，手动放输入 + 拆相机，让上层继续走。
// Postfix 的手动释放只在原方法**没清干净**（_game/_gameWorld 有残留）时才动手，不与原实现重复。

[HarmonyPatch(typeof(TarkovApplication.NarrateController), "Hide")]
public static class NarrateHideGuard
{
    static void Prefix()
    {
        Plugin.Log.LogDebug("[narrate] >>> controller.Hide");
        ForeignLights.Restore();
        TexturePin.Restore();
        DecalDraw.Reset();
        DecalGuard.Clear();
        TabRouter.UnwatchNarrate();   // 09-07 终审：不退订的话之后任何对话里的「任务」动作都会开上一位商人的任务页
        RaidFlagReset.Run();   // 09-07：MenuOverhaul 之流把访问当战局、退出后装死——在主菜单回来之前把它们的战局标记复位
        NarrateEntry.EnsureMenu();
    }

    // Harmony 规则：原方法抛异常时 Postfix 不执行、只有 Finalizer 执行（09-07 终审）。下面的残留清理原本只挂在 Postfix 上，
    // 恰恰在它要救的那种情况（Hide 半途炸掉）不会跑：_game 残留 → GameExist 恒真 → Narrating.Now 全局卡死，
    // 之后进战局 FOV 锁 / FiR 拦截 / 对话条件篡改 / 曝光钉住全部在战局里生效。所以正常路和异常路都调同一份 Teardown。
    // ⚠️ 这个共用方法**不能叫 Cleanup**：那是 Harmony 保留的钩子名，挂补丁时会拿空参数调它 → 空引用 → 日志报「NarrateHideGuard 挂载失败」
    // （09-10 从日志抓到，38/39；进/出补丁其实已挂上，只是收尾钩子炸了）。同理别用 Prepare / TargetMethod / Cleanup 当普通方法名。
    static void Postfix(TarkovApplication.NarrateController __instance) => Teardown(__instance);

    static void Teardown(TarkovApplication.NarrateController __instance)
    {
        var game = __instance._game;
        var gameWorld = __instance._gameWorld;
        if (game == null && gameWorld == null) return;
        try { game?.Stop(); }
        catch (Exception ex) { Plugin.Log.LogWarning("[narrate] game stop failed: " + ex.Message); }
        if (gameWorld != null)
        {
            Singleton<GameWorld>.Release(gameWorld);
            Singleton<IGameLevel>.Release(gameWorld);
            UnityEngine.Object.Destroy(gameWorld.gameObject);
        }
        __instance._game = null;
        __instance._gameWorld = null;
        __instance._unsubscriber = new CompositeDisposable();
        // ⚠️ BetterAudio 是 GameWorld 的子物体(ClientGameWorld)，上面的 Destroy 会连它一起销毁；
        // 而 GameWorld.OnDestroy 是空的，Raid 生命周期的 DataProvider 容器没人清。
        // 不清的话，下一个世界(二次访问/战局)新建的 BetterAudio 会因容器已存在而不再给
        // AudioMixerData 赋值(BetterAudio.PreloadCoroutine 只在 CreateData 成功时赋值)，
        // 之后 ToggleNarrate / FadeInVolumeBeforeRaid 全部 NRE —— 战局倒计时后启动协程
        // 当场死亡，表现为"倒计时完毕进不去"。这里补上引擎战局结束时自己会做的那一步
        // (GameWorld.Dispose 里的 DataProvider.Instance.Dispose())。
        try { EFT.DataProviding.DataProvider.Instance.Dispose(); }
        catch (Exception ex2) { Plugin.Log.LogWarning("[narrate] data provider cleanup failed: " + ex2.Message); }
        Plugin.Instance.StartCoroutine(UnloadScenes());
    }

    static IEnumerator UnloadScenes()
    {
        Task task = TarkovApplication.NarrateController.Scenes.UnloadAll();
        while (!task.IsCompleted) yield return null;
        AmbientGuard.Clear();   // 场景刚卸完，此刻表里的死光源最全
        Plugin.Log.LogInfo("[narrate] world torn down + vendor scenes unloaded");
    }

    static Exception Finalizer(TarkovApplication.NarrateController __instance, Exception __exception)
    {
        if (__exception == null) { Plugin.Log.LogDebug("[narrate] <<< controller.Hide ok"); return null; }
        Plugin.Log.LogWarning("[narrate] <<< controller.Hide faulted (swallowed): " + __exception.Message);
        try { Teardown(__instance); }
        catch (Exception ex) { Plugin.Log.LogError("[narrate] teardown after faulted Hide failed: " + ex); }
        return null;
    }
}

[HarmonyPatch(typeof(NarrateGame), "Hide")]
public static class NarrateGameHideGuard
{
    static Exception Finalizer(NarrateGame __instance, Exception __exception)
    {
        if (__exception == null) { Plugin.Log.LogDebug("[narrate] <<< game.Hide ok"); return null; }
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
