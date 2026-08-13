using Comfort.Common;
using EFT;
using EFT.Dialogs;
using HarmonyLib;

namespace VisitAPI.Native;

[HarmonyPatch(typeof(BaseTraderDialogController), "InitNewDialog")]
public static class NarrateDialogEntryGuard
{
	private static void Prefix(BaseTraderDialogController __instance, MongoID dialogId)
	{
		if (Singleton<GameWorld>.Instantiated && Singleton<GameWorld>.Instance is NarrateGameWorld)
		{
			if (DialogStorage.Instance != null && DialogStorage.Instance.TryGetTemplate(dialogId, out var template))
			{
				template.CanBeFirstDialog = true;
			}
			RetailDialogs.SeedVariables(__instance);
			Plugin.Log.LogDebug("[narrate] dialog entry unlocked + session variables seeded for " + dialogId);
		}
	}
}
