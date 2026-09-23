using System;
using System.Collections;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using HarmonyLib;

namespace VisitAPI.Native;

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
        PostChain.Clear();

        TabRouter.UnwatchNarrate();
        RaidFlagReset.Run();
        NarrateEntry.EnsureMenu();
    }

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
        try { EFT.DataProviding.DataProvider.Instance.Dispose(); }
        catch (Exception ex2) { Plugin.Log.LogWarning("[narrate] data provider cleanup failed: " + ex2.Message); }
        Plugin.Instance.StartCoroutine(UnloadScenes());
    }

    static IEnumerator UnloadScenes()
    {
        Task task = TarkovApplication.NarrateController.Scenes.UnloadAll();
        while (!task.IsCompleted) yield return null;
        AmbientGuard.Clear();
        Plugin.Log.LogInfo("[narrate] world torn down + vendor scenes unloaded");
    }

    static Exception Finalizer(TarkovApplication.NarrateController __instance, Exception __exception, ref Task __result)
    {
        if (__exception == null) { Plugin.Log.LogDebug("[narrate] <<< controller.Hide ok"); return null; }
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
