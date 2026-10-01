using System;
using System.Linq;
using System.Reflection;
using EFT;
using EFT.Quests;
using HarmonyLib;

namespace VisitAPI.Native;

/// <summary>10-01（SORA：疗养院的碰面点别提前露在动态地图上）：Dynamic Maps 给「进行中」任务的每个未完成目标画点，不看目标现在显不显示——
/// 靠 visibilityConditions 藏着的后续目标从接下任务起就标在图上。它筛目标用 QuestUtils.IsConditionCompleted（true = 不画），
/// 这里在它后面补一句：任务页上还没显示出来的目标也不画。
/// 它是可选模组：没装就什么都不做；它的程序集可能比我们晚加载，所以等第一次进战局再挂（TriggerHost）。</summary>
public static class DynamicMapsHidden
{
    static bool _tried;

    public static void Ensure()
    {
        if (_tried) return;
        _tried = true;
        try
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "DynamicMaps")?.GetType("DynamicMaps.Utils.QuestUtils");
            if (type == null) return;
            var target = type.GetMethod("IsConditionCompleted", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (target == null) { Plugin.Log.LogWarning("[map] Dynamic Maps is installed but QuestUtils.IsConditionCompleted was not found (different version?); hidden objectives will still be drawn on its map"); return; }
            new Harmony("com.sora.visitapi").Patch(target, postfix: new HarmonyMethod(typeof(DynamicMapsHidden), nameof(Postfix)));
        }
        catch (Exception e) { Plugin.Log.LogWarning("[map] hooking Dynamic Maps failed (its map works as before): " + e.Message); }
    }

    static void Postfix(Player player, QuestDataClass questData, Condition condition, ref bool __result)
    {
        if (__result || player == null || questData == null || condition == null) return;
        try
        {
            var quest = player.QuestController?.Quests?.GetConditional(questData.Id);
            if (quest != null && !quest.CheckVisibilityStatus(condition)) __result = true;
        }
        catch { }
    }
}
