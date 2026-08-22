using EFT.CameraControl;
using HarmonyLib;

namespace VisitAPI.Native;

[HarmonyPatch(typeof(CameraManager), "IsActive", MethodType.Setter)]
public static class CameraNullGuard
{
	private static bool Prefix(CameraManager __instance)
	{
		if (__instance.Camera != null)
		{
			return true;
		}
		Plugin.Log.LogDebug("[narrate] IsActive ignored - no camera bound");
		return false;
	}
}
