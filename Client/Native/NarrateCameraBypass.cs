using Comfort.Common;
using EFT;
using EFT.CameraControl;
using HarmonyLib;

namespace VisitAPI.Native;

[HarmonyPatch(typeof(PlayerCameraController), "Create")]
public static class NarrateCameraBypass
{
	private static bool Prefix(ref PlayerCameraController __result)
	{
		if (!Singleton<GameWorld>.Instantiated || !(Singleton<GameWorld>.Instance is NarrateGameWorld))
		{
			return true;
		}
		Plugin.Log.LogDebug("[narrate] native FPS camera creation skipped");
		__result = null;
		return false;
	}
}
