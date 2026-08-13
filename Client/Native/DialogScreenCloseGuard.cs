using System;
using EFT.UI;
using HarmonyLib;

namespace VisitAPI.Native;

[HarmonyPatch(typeof(TraderDialogScreen), "Close")]
public static class DialogScreenCloseGuard
{
	private static Exception Finalizer(Exception __exception)
	{
		if (__exception == null)
		{
			Plugin.Log.LogDebug("[narrate] <<< dialog screen closed");
		}
		else
		{
			Plugin.Log.LogWarning("[narrate] <<< dialog screen close faulted (swallowed): " + __exception.Message);
		}
		return null;
	}
}
