using System;
using System.Collections;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace VisitAPI.Native;

[HarmonyPatch(typeof(TarkovApplication.NarrateController), "Hide")]
public static class NarrateHideGuard
{
	private static void Prefix()
	{
		Plugin.Log.LogDebug("[narrate] >>> controller.Hide");
		NarrateEntry.EnsureMenu();
	}

	private static void Postfix(TarkovApplication.NarrateController __instance)
	{
		NarrateGame game = __instance._game;
		NarrateGameWorld gameWorld = __instance._gameWorld;
		if (!(game == null) || !(gameWorld == null))
		{
			try
			{
				game?.Stop();
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[narrate] game stop failed: " + ex.Message);
			}
			if (gameWorld != null)
			{
				Singleton<GameWorld>.Release(gameWorld);
				Singleton<IGameLevel>.Release(gameWorld);
				UnityEngine.Object.Destroy(gameWorld.gameObject);
			}
			__instance._game = null;
			__instance._gameWorld = null;
			__instance._unsubscriber = new CompositeDisposable();
			Plugin.Instance.StartCoroutine(UnloadScenes());
		}
	}

	private static IEnumerator UnloadScenes()
	{
		Task task = TarkovApplication.NarrateController.Scenes.UnloadAll();
		while (!task.IsCompleted)
		{
			yield return null;
		}
		Plugin.Log.LogInfo("[narrate] world torn down + vendor scenes unloaded");
	}

	private static Exception Finalizer(Exception __exception)
	{
		if (__exception == null)
		{
			Plugin.Log.LogDebug("[narrate] <<< controller.Hide ok");
		}
		else
		{
			Plugin.Log.LogWarning("[narrate] <<< controller.Hide faulted (swallowed): " + __exception.Message);
		}
		return null;
	}
}
