using System;
using EFT.Dialogs;
using HarmonyLib;

namespace VisitAPI.Native;

// 点击管线会静默吞构建异常, 此处 LogError 是唯一现场可见性
[HarmonyPatch(typeof(DynamicTraderDialog), MethodType.Constructor, typeof(TraderDialogTemplate), typeof(IDialogContext))]
public static class NarrateDialogBuildGuard
{
	private static Exception Finalizer(Exception __exception)
	{
		if (__exception != null)
		{
			Plugin.Log.LogError("[narrate] dialog build failed: " + __exception);
		}
		return __exception;
	}
}
