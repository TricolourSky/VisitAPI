using EFT.Quests;
using EFT.UI;
using HarmonyLib;

namespace VisitAPI.Native;

/// <summary>剧情任务不进列表（1.1：章节和它的子任务只住「剧情」页，支线 / 商人任务列表里没有它们，也不计入商人的任务数）。
/// 支线列表 = `TasksScreen.IsRegularQuest` 那道过滤器；商人任务页和计数都看 `Quest.IsVisible`，在它的 getter 上再拦一道
/// （IsVisible 原本只表示 AvailableAfter 倒计时到没到，任务进不进任务书不看它，所以改它不影响接/交/自动链）。
/// BepInEx 配置 Chapter.HideStoryQuestsInLists 可关。DEV_NOTES #74。</summary>
public static class StoryList
{
    public static bool Hidden(Quest q) => Plugin.HideStory.Value && q != null && QuestFlags.IsStory(q.Id);

    [HarmonyPatch(typeof(TasksScreen), nameof(TasksScreen.IsRegularQuest))]
    static class SideList { static void Postfix(Quest quest, ref bool __result) { if (__result && Hidden(quest)) __result = false; } }

    [HarmonyPatch(typeof(Quest), nameof(Quest.IsVisible), MethodType.Getter)]
    static class TraderList { static void Postfix(Quest __instance, ref bool __result) { if (__result && Hidden(__instance)) __result = false; } }
}
