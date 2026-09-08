using System.Collections.Generic;
using System.Linq;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;

namespace VisitAPI.Native;

/// <summary>剧情任务不进列表（1.1：章节和子任务只住「剧情」页，支线/商人任务列表里没有它们，也不计入商人任务数）。
/// 支线列表 = `TasksScreen.IsRegularQuest` 那道过滤器；商人任务页和计数 = `QuestBook.GetQuests(traderId)`（GetQuestsCount 两个重载也只经它）。
/// 09-07 终审：以前挂在 `Quest.IsVisible` getter 上——引擎自己也读这个 getter（`Quest.AwaitVisible`：`QuestStatus == AvailableAfter &amp;&amp; IsVisible`
/// 才把任务从「等待重开」推回「可接」），剧情任务的 getter 恒 false = 倒计时到了也永远回不来。改成只过滤列表本身，不碰 getter。
/// BepInEx 配置 Chapter.HideStoryQuestsInLists 可关。DEV_NOTES #74。</summary>
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

    /// <summary>商人页的任务列表（「接受」那一栏，QuestsListView）不走 GetQuests——它自己 `Quests.BindWhere(x => x.IsVisible && TraderId == …)`，
    /// 每一行的显隐由 UpdateSingleQuestVisibility 定。09-08 SORA 实机：塔科夫之旅的任务和章节都顶着「&lt;id&gt; name」露在 Prapor / Ragman 列表里。
    /// 在这一处把剧情家族的行藏掉；计数文字走 GetQuestsCount → GetQuests，上面那条已经滤过。</summary>
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
