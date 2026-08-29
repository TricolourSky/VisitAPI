using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Dialogs;
using EFT.Quests;
using HarmonyLib;

namespace VisitAPI.Native;

[HarmonyPatch(typeof(QuestStatusCondition), "Test")]
public static class NarrateQuestGhostGuard
{
	// 库外任务统一模拟为单一状态: 默认 Locked; 需要特定人设的商人按表指定(每任务恒定一个状态, 互斥分支才不会同时放行, 详见 DEV_NOTES #49)
	// Skier 的 Started 覆写已撤销: 真正缺的是相识变量(KnownSeeds), 覆写反而让任务阶段台词顶掉正常开场白(#58)
	private static readonly Dictionary<string, EQuestStatus> SimStatus = new();

	private static bool Prefix(QuestStatusCondition __instance, IDialogContext context, ref bool __result)
	{
		if (!Singleton<GameWorld>.Instantiated || !(Singleton<GameWorld>.Instance is NarrateGameWorld))
		{
			return true;
		}
		if (context?.QuestsData != null && context.QuestsData.Any((QuestDataClass q) => q.Id == __instance.QuestId))
		{
			return true;
		}
		if (!SimStatus.TryGetValue(__instance.QuestId, out var sim))
		{
			sim = EQuestStatus.Locked;
		}
		__result = __instance.Statuses != null && __instance.Statuses.Contains(sim);
		return false;
	}
}
