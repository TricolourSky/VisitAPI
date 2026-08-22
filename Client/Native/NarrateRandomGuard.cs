using Comfort.Common;
using EFT;
using EFT.Dialogs;
using HarmonyLib;

namespace VisitAPI.Native;

[HarmonyPatch(typeof(RandomLineCondition), "Test")]
public static class NarrateRandomGuard
{
	private static bool Prefix(ref bool __result)
	{
		if (!Singleton<GameWorld>.Instantiated || !(Singleton<GameWorld>.Instance is NarrateGameWorld))
		{
			return true;
		}
		__result = true;
		return false;
	}
}
