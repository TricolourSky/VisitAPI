using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT;
using HarmonyLib;

namespace VisitAPI.Native;

[HarmonyPatch(typeof(NPCObject), "Get")]
public static class NarrateNpcGuard
{
	private static readonly FieldInfo Npcs = AccessTools.Field(typeof(NPCObject), "_npcs");

	private static bool Prefix(Profile.ETraderType source, ref NPCObject __result)
	{
		Dictionary<Profile.ETraderType, NPCObject> npcs = (Dictionary<Profile.ETraderType, NPCObject>)Npcs.GetValue(null);
		if (npcs == null || npcs.ContainsKey(source))
		{
			return true;
		}
		if (!TarkovApplication.NarrateController.Scenes.IsValid(source, out var info))
		{
			return true;
		}
		NPCObject match = npcs.Values.FirstOrDefault((NPCObject n) => n != null && n.gameObject.scene.name == info.sceneName);
		if (match == null)
		{
			return true;
		}
		Plugin.Log.LogInfo("[narrate] NPC matched by scene for " + source);
		__result = match;
		return false;
	}
}
