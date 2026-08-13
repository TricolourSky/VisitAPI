using Comfort.Common;
using EFT;
using HarmonyLib;

namespace VisitAPI.Native;

[HarmonyPatch(typeof(Profile), "SetSpawnedInSession")]
public static class FirGuard
{
	private static bool Prefix(bool value)
	{
		if (value || !Singleton<GameWorld>.Instantiated || !(Singleton<GameWorld>.Instance is NarrateGameWorld))
		{
			return true;
		}
		Plugin.Log.LogDebug("[narrate] blocked FiR wipe during visit");
		return false;
	}
}
