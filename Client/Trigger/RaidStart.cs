using System;
using EFT;
using HarmonyLib;

namespace VisitAPI.Native;

/// <summary>进图开局（GameWorld.OnGameStarted）要做的几件事，一个补丁、每件各自兜住，顺序：
/// 任务区域 → 剧情刷兵 → 护卫 → 盯住战局背包里的开章物品。
/// 访问世界不算：原生 Show 里那一次 OnGameStarted 已被 NarrateStartBroadcast 转译掉，这里再挡一道，转译失败时靠它兜底。</summary>
[HarmonyPatch(typeof(GameWorld), nameof(GameWorld.OnGameStarted))]
public static class RaidStart
{
    static void Postfix(GameWorld __instance)
    {
        if (Narrating.IsVisitWorld(__instance) || Narrating.Now) return;
        Step(() => QuestZones.Spawn(__instance), "[zones] failed to spawn quest zones (raid unaffected): ");
        Step(() => { StoryRush.Reset(); BotLayers.Reset(); StorySpawns.Spawn(__instance); }, "[spawns] failed to start story spawns (raid unaffected): ");
        Step(() => Escort.Begin(__instance), "[escort] failed to start the escort check (raid unaffected): ");
        // 10-05：拿到物品就开的任务——进图时盯上战局里的背包（战局里任务状态事件不一定在捡东西之前来，ChapterChain 那边挂不上）
        Step(() => ItemStart.Watch(GamePlayerOwner.MyPlayer?.QuestController), "[chain] Watching the raid inventory for quest-start items failed (raid unaffected): ");
    }

    static void Step(Action act, string failure)
    {
        try { act(); }
        catch (Exception e) { Plugin.Log.LogError(failure + e); }
    }
}
