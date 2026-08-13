using System;
using HarmonyLib;

namespace VisitAPI.Native;

[HarmonyPatch(typeof(BetterAudio), nameof(BetterAudio.ToggleNarrate))]
public static class NarrateAudioGuard
{
	private static Exception Finalizer(Exception __exception)
	{
		if (__exception != null)
		{
			Plugin.Log.LogWarning("[narrate] audio toggle skipped (" + __exception.GetType().Name + "): " + __exception.Message);
		}
		return null;
	}
}
