using EFT.CameraControl;
using HarmonyLib;

namespace VisitAPI.Native;

[HarmonyPatch(typeof(CameraManager), "IsActive", MethodType.Getter)]
public static class CameraNullGetterGuard
{
	private static bool Prefix(CameraManager __instance, ref bool __result)
	{
		if (__instance.Camera != null)
		{
			return true;
		}
		__result = false;
		return false;
	}
}
