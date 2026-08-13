using System.Collections.Generic;
using System.Reflection;
using EFT.CameraControl;
using HarmonyLib;
using UnityEngine;

namespace VisitAPI.Native;

[HarmonyPatch]
public static class UpscalerGuard
{
	private static readonly FieldInfo SsaaImplField = AccessTools.Field(typeof(CameraManager), "_ssaaImpl");

	private static IEnumerable<MethodBase> TargetMethods()
	{
		return new MethodBase[4]
		{
			AccessTools.Method(typeof(CameraManager), "SetFSR"),
			AccessTools.Method(typeof(CameraManager), "SetFSR2"),
			AccessTools.Method(typeof(CameraManager), "SetFSR3"),
			AccessTools.Method(typeof(CameraManager), "SetDLSSPreset")
		};
	}

	private static bool Prefix(CameraManager __instance)
	{
		if (SsaaImplField == null || SsaaImplField.GetValue(__instance) as Object != null)
		{
			return true;
		}
		Plugin.Log.LogDebug("[narrate] camera has no SSAAImpl - upscaler setup skipped");
		return false;
	}
}
