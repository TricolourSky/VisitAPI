using System.Collections.Generic;
using System.Linq;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;

namespace VisitAPI.Native;

public static class StoryList
{
    public static bool Hidden(Quest q) => Plugin.HideStory.Value && q != null && QuestFlags.IsStory(q.Id);

    [HarmonyPatch(typeof(TasksScreen), nameof(TasksScreen.IsRegularQuest))]
    public static class SideList { static void Postfix(Quest quest, ref bool __result) { if (__result && Hidden(quest)) __result = false; } }

    [HarmonyPatch(typeof(QuestBook), nameof(QuestBook.GetQuests), typeof(string))]
    public static class TraderList
    {
        static void Postfix(ref IEnumerable<Quest> __result)
        {
            if (__result != null && Plugin.HideStory.Value) __result = __result.Where(q => !Hidden(q));
        }
    }

    [HarmonyPatch(typeof(QuestsListView), nameof(QuestsListView.UpdateSingleQuestVisibility))]
    public static class TraderQuestList
    {
        static void Postfix(QuestListItem questView)
        {
            try { if (questView != null && Hidden(questView.Quest)) questView.gameObject.SetActive(false); }
            catch (System.Exception e) { Plugin.Log.LogError("[story] 商人任务列表过滤失败: " + e); }
        }
    }
}
