using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EFT;
using EFT.InventoryLogic;
using EFT.Quests;
using HarmonyLib;
using JsonType;

namespace VisitAPI.Native;

[HarmonyPatch(typeof(GameWorld), nameof(GameWorld.ManageQuestLoot))]
public static class QuestLootGate
{
    static bool Prefix(GameWorld __instance, Player player, List<JsonLootItem> ____questLootItems, ref Task __result)
    {
        try
        {
            if (player?.Profile?.Inventory == null || ____questLootItems == null || ____questLootItems.Count == 0) return true;
            var playerItems = player.Profile.Inventory.GetPlayerItems(EPlayerItems.QuestItems);
            if (playerItems == null) return true;
            var owned = playerItems.Select(x => x.TemplateId).ToList();
            var started = player.QuestController?.Quests?.Where(q => q.QuestStatus == EQuestStatus.Started).ToList();
            if (started == null) return true;
            var gated = new List<string>();
            foreach (var loot in ____questLootItems)
            {
                if (loot?.Item == null || owned.Contains(loot.Item.TemplateId)) continue;
                loot.ValidProfiles = new MongoID[20];
                var num = 0;
                foreach (var quest in started)
                {
                    try
                    {
                        foreach (var c in quest.GetConditions<ConditionFindItem>(EQuestStatus.AvailableForFinish))
                        {
                            if (c.target == null || !c.target.Contains(loot.Item.StringTemplateId)) continue;
                            if (quest.CompletedConditions.Contains(c.id)) continue;
                            if (quest.ProgressCheckers.TryGetValue(c, out var pc) && pc.Test()) continue;
                            if (QuestFlags.IsStory(quest.Id) && !quest.CheckVisibilityStatus(c)) { gated.Add(loot.Item.StringTemplateId + "@" + quest.Id); continue; }
                            if (num < loot.ValidProfiles.Length) loot.ValidProfiles[num++] = player.ProfileId;
                        }
                    }
                    catch (Exception e) { Plugin.Log.LogWarning("[quest-loot] 任务 " + quest.Id + " 的条件检查出错，跳过: " + e.Message); }
                }
                if (num == 0) loot.ValidProfiles = null;
                __instance.SpawnLootItem(loot, true, player);
            }
            if (gated.Count > 0) Plugin.Log.LogInfo("[quest-loot] 目标还没出现、本局先不显示的任务物品: " + string.Join(", ", gated.Distinct()));
            __result = Task.CompletedTask;
            return false;
        }
        catch (Exception e) { Plugin.Log.LogWarning("[quest-loot] 接管失败，走原生: " + e.Message); return true; }
    }
}
