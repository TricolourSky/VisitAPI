using System;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using UnityEngine;

namespace VisitAPI.Native;

[HarmonyPatch(typeof(NarrateGame), "Hide")]
public static class NarrateGameHideGuard
{
	private static Exception Finalizer(NarrateGame __instance, Exception __exception)
	{
		if (__exception == null)
		{
			Plugin.Log.LogDebug("[narrate] <<< game.Hide ok");
			return null;
		}
		Plugin.Log.LogWarning("[narrate] <<< game.Hide faulted");
		Plugin.Log.LogWarning("[narrate] game.Hide faulted (recovering): " + __exception.Message);
		EftGamePlayerOwner playerOwner = __instance.PlayerOwner;
		if ((UnityEngine.Object)(object)playerOwner != null)
		{
			try
			{
				playerOwner.vmethod_1();
			}
			catch (Exception ex)
			{
				Plugin.Log.LogWarning("[narrate] input release failed: " + ex.Message);
			}
			try
			{
				PlayerCameraController.Destroy(playerOwner.Player);
			}
			catch (Exception ex2)
			{
				Plugin.Log.LogWarning("[narrate] camera teardown failed: " + ex2.Message);
			}
		}
		return null;
	}
}
