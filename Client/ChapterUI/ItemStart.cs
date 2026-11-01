using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.InventoryLogic;
using EFT.Quests;
using HarmonyLib;
using JsonType;
using Newtonsoft.Json.Linq;

namespace VisitAPI.Native;

/// <summary>10-05 神秘蓝焰：1.1 的「读到这件物品就开这条任务」（任务的 AutoStart 桶里一串 CompletableItem，读到任意一件就开——SPT5 服务端
/// GetCompletableItemQuests 核过）。0.16 没有阅读这个动作，照原生「找到物品」目标的口径改成「拿到」（SORA 10-05：「捡到就行」）：
/// 物品进了背包（战局里捡起来、大厅里收邮件）当场接；仓库里本来就有的，由 ChapterChain 在任务进入「可接」时查一遍仓库补接。
/// 物品清单是 adapt 从 AutoStart 桶搬进 visitapi.startOnItems 的；范围和原生 ConditionsConnectorsManager.GetItemsForConditionIEnumerable 一样（藏身处存储除外）。</summary>
public static class ItemStart
{
    static InventoryController _watched;

    /// 跟着活的任务控制器挂背包的「物品放进来」事件（原生 FindItem 目标也是听这个）；换了控制器（进战局 / 回大厅）就把旧的摘掉
    public static void Watch(QuestController qc)
    {
        var inv = qc?.InventoryController;
        if (inv == null || ReferenceEquals(inv, _watched)) return;
        if (_watched != null) _watched.AddItemEvent -= OnAdded;
        _watched = inv;
        inv.AddItemEvent += OnAdded;
    }

    static void OnAdded(AddItemEventArgs e)
    {
        try
        {
            if (e == null || e.Status != CommandStatus.Succeed || e.Item == null) return;
            var ids = QuestFlags.StartedBy(e.Item.StringTemplateId);
            if (ids.Count == 0) return;
            var qc = QuestOps.Resolve();
            if (qc?.Quests == null || !ReferenceEquals(qc.InventoryController, _watched)) return;
            foreach (var id in ids) ChapterChain.StartByItem(qc, qc.Quests.GetConditional(id));
        }
        catch (Exception ex) { Plugin.WarnOnce("itemstart/added", "[chain] Starting a quest from a picked-up item failed (the item itself is unaffected): " + ex.Message); }
    }

    /// 背包 / 仓库里有没有清单里的任意一件
    public static bool Has(QuestController qc, List<string> tpls)
    {
        var inv = qc?.InventoryController?.Inventory;
        if (inv == null || tpls == null || tpls.Count == 0) return false;
        return inv.GetPlayerItems(EPlayerItems.AllExceptHideoutStashes).Any(i => i != null && tpls.Contains(i.StringTemplateId));
    }

    // 进图开局换成战局里本机玩家的背包：挂在 RaidStart（10-24 合并）

    /// <summary>10-06 SORA：「这个应该是进入到日记物品，不应该出现在玩家背包内」——开章线索在 1.1 里是可读物品，捡起来进日记、不占背包。
    /// 0.16 没有日记，包里把它们改成任务物品（QuestItem = true，捡起来进任务物品栏）。可原生 GameWorld.ManageQuestLoot 只给「有进行中任务的找物品目标要它」的人刷任务物品，
    /// 开章线索在开章前没人要，会谁也刷不出来。1.1 的 ManageQuestLoot 对这类「个人」可读物品另有一套（1.1.0 GameAssembly 反汇编核过）：
    /// 阵营允许、没找到过、等级够、要求先找到的物品都找到过、要求的任务都处于指定状态（进行中 / 成功 / 失败，任务不在档里即不满足），全部满足才给这名玩家刷。
    /// 这里只给开章物品补这一条（规矩由服务端随 flags 带来）：原生算完、真正刷出来之前，满足就把这名玩家加进可见名单；原生已经让他看得到的不动。
    /// 章节步骤里的笔记 / 录音带照旧走原生「有人要才刷」——0.16 没有「找到过」的记录，任务物品被收走后它们会按 1.1 的条件又刷回来</summary>
    [HarmonyPatch(typeof(GameWorld), nameof(GameWorld.SpawnLootItem))]
    public static class Visible
    {
        static void Prefix(JsonLootItem lootItem, Player questPlayer)
        {
            try
            {
                if (questPlayer == null || lootItem?.Item == null) return;
                var tpl = lootItem.Item.StringTemplateId;
                var rule = QuestFlags.StartItemRule(tpl);
                if (rule == null) return;
                MongoID me = questPlayer.ProfileId;
                if (lootItem.ValidProfiles != null && lootItem.ValidProfiles.Contains(me)) return;
                if (!rule.Allows(questPlayer, tpl)) return;
                lootItem.ValidProfiles = lootItem.ValidProfiles == null ? new[] { me } : lootItem.ValidProfiles.Concat(new[] { me }).ToArray();
            }
            catch (Exception e) { Plugin.WarnOnce("itemstart/visible", "[chain] Deciding whether a story-start item shows up for this player failed (the item stays hidden this raid): " + e.Message); }
        }
    }

    /// <summary>1.1 CompletableTemplate 的可见条件（QuestConditions / CompletableConditions / AllowBear·Usec·Savage / RequiredLevel），服务端 ItemLoader 从包里的物品模板读出</summary>
    public sealed class Rule
    {
        public sealed class QuestReq { public string Id; public bool Started, Completed, Failed; }
        public readonly List<QuestReq> Quests = new();
        /// CompletableConditions 里的物品要先找到过（0.16 没有「读完」这回事，要求读完的也按找到算）
        public readonly List<string> Found = new();
        public bool Bear, Usec, Savage;
        public int Level;

        public static Rule Parse(JObject o)
        {
            var r = new Rule { Bear = o.Value<bool?>("bear") ?? false, Usec = o.Value<bool?>("usec") ?? false, Savage = o.Value<bool?>("savage") ?? false, Level = o.Value<int?>("level") ?? 0 };
            if (o["quests"] is JArray qs)
                foreach (var q in qs.OfType<JObject>())
                    if (q.Value<string>("id") is { Length: > 0 } id)
                        r.Quests.Add(new QuestReq { Id = id, Started = q.Value<bool?>("started") ?? false, Completed = q.Value<bool?>("completed") ?? false, Failed = q.Value<bool?>("failed") ?? false });
            if (o["items"] is JArray its)
                foreach (var i in its.OfType<JObject>())
                    if (i.Value<string>("tpl") is { Length: > 0 } t) r.Found.Add(t);
            return r;
        }

        public bool Allows(Player p, string tpl)
        {
            var side = p.Side;
            if (!(side == EPlayerSide.Bear ? Bear : side == EPlayerSide.Usec ? Usec : Savage)) return false;
            var items = p.Profile?.Inventory?.GetPlayerItems(EPlayerItems.All);
            if (items == null) return false;
            var have = new HashSet<string>(items.Where(i => i != null).Select(i => i.StringTemplateId));
            if (have.Contains(tpl)) return false;   // 1.1：找到过就不再刷；0.16 没有「找到过」的记录，以身上 / 仓库 / 任务物品栏里有没有为准
            if ((p.Profile?.Info?.Level ?? 0) < Level) return false;
            if (Found.Any(t => !have.Contains(t))) return false;
            var book = p.QuestController?.Quests;
            foreach (var q in Quests)
            {
                var st = book?.GetConditional(q.Id)?.QuestStatus;
                if (st == null) return false;   // 任务不在档里（比如还没移植的「他们已经来了」）= 不满足，和 1.1 一样
                var s = st.Value;
                // 1.1 只认 Fail；0.16 大厅会把服务端的失败记成 MarkedAsFailed / FailRestartable（同 ChapterChain.StatusMatches）
                if (!((q.Started && s == EQuestStatus.Started) || (q.Completed && s == EQuestStatus.Success) || (q.Failed && ChapterUI.ChapterStates.FailLike(s)))) return false;
            }
            return true;
        }
    }
}
