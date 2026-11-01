using System.Collections.Generic;
using System.Linq;
using EFT.InventoryLogic;
using EFT.Quests;
using VisitAPI.Native;

namespace VisitAPI.ChapterUI
{
    /// <summary>10-15 SORA 实机（章节二「重返海岸线」：U 盘已在任务物品栏，「在战局中找到 ARRS 站点 U 盘」却不打勾）：
    /// #183 当天定了因——任务物品不带「战局中找到」（FiR）标记，改的是数据不是代码；探针按 #183 的决定留着给同类问题取证。
    /// 只在「找到物品」目标显示未完成、而玩家物品里明明有目标物品时写一条警告，把计数器状态、每件物品的 FiR / 所在容器、任务阵营和控制器类型都带上。
    /// 每条目标只写一次，正常流程一行不打。</summary>
    public static class FindItemProbe
    {
        static readonly HashSet<string> _logged = new();

        public static void Check(Quest quest, Condition cond, ChapterStates.ERow state)
        {
            try
            {
                if (state != ChapterStates.ERow.Active || cond is not ConditionFindItem find || quest == null) return;
                if (!ChapterStates.Running(quest.QuestStatus)) return;
                var key = quest.Id + "|" + cond.id;
                if (_logged.Contains(key)) return;
                var inventory = ChapterTab.Inventory?.Inventory;
                if (inventory == null || find.target == null) return;
                var held = inventory.GetPlayerItems(EPlayerItems.AllExceptHideoutStashes)
                    .Where(i => i != null && find.target.Contains(i.StringTemplateId)).ToList();
                if (held.Count == 0) return;
                _logged.Add(key);
                var checker = quest.ProgressCheckers.TryGetValue(cond, out var pc) ? pc : null;
                var items = string.Join("; ", held.Select(i => $"{i.StringTemplateId} fir={i.MarkedAsSpawnedInSession} container={i.CurrentAddress?.Container?.GetType().Name ?? "?"} parent={i.CurrentAddress?.Container?.ParentItem?.StringTemplateId ?? "?"}"));
                var qc = ChapterTab.Quests;
                Plugin.Log.LogWarning($"[quest] FindItem objective {cond.id} of quest {quest.Id} shows undone although the player holds {held.Count} matching item(s): " +
                    $"checker={(checker == null ? "none" : $"getter={checker.HasGetter()} current={checker.CurrentValue} test={checker.Test()}")} value={cond.value} " +
                    $"onlyFiR={find.onlyFoundInRaid} countInRaid={find.countInRaid} completed={quest.CompletedConditions.Contains(cond.id)} questSide={quest.PlayerSide} " +
                    $"profileSide={ChapterTab.Profile?.Side} controller={qc?.GetType().Name ?? "?"} items=[{items}]");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("[quest] FindItem probe failed (display unaffected): " + e.Message); }
        }
    }
}
