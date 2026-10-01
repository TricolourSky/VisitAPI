using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Quests;
using HarmonyLib;

namespace VisitAPI.Native;

/// <summary>10-01（SORA 实测：击杀 Shturman 的奖励邮件寄了一份、战局里又多给一份）：原生的局内交任务
/// （QuestControllerClientLocalGame.FinishQuest → ItemManipulator.FinishConditional）会当场把 Success 奖励里的物品塞进背包、
/// 给本地档案加经验和声望；服务端 StoryRaidRewards 在结算时又把内容包任务的奖励完整发一遍（邮件），于是双份。
/// 内容包的任务（服务端 flags 里的 pack）统一由结算发：局内交的那一下，把这三类奖励从任务的 Success 清单里暂时拿掉，交完放回。
/// 背包没位置时原生会让整条交任务失败，不塞物品也就没有这个问题。
/// 服务端没带 pack 标记（旧服务端）时这里什么都不动，行为和以前一样，宁可重复也不丢奖励。
/// 原生的发奖在 FinishQuest 的第一个 await 之前跑完，Prefix / Finalizer 正好把它括住。
/// Fika 的 ClientQuestController 重写了 FinishQuest：先读一遍奖励物品、交完广播给队友往这名玩家身上加——所以它那一层也要括住，
/// 不然联机时队友会看到并不存在的奖励物品；它是可选模组、可能比我们晚加载，等第一次进战局再挂（TriggerHost）。</summary>
public static class RaidRewardHold
{
    static int _depth;
    static Quest _quest;
    static IReadOnlyList<QuestReward> _full;
    static bool _fikaTried;

    [HarmonyPatch(typeof(QuestControllerClientLocalGame), nameof(QuestControllerClientLocalGame.FinishQuest))]
    public static class Finish
    {
        static void Prefix(Quest quest) => Enter(quest);
        static void Finalizer() => Exit();
    }

    public static void EnsureFika()
    {
        if (_fikaTried) return;
        _fikaTried = true;
        try
        {
            var fika = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Fika.Core");
            if (fika == null) return;
            var target = fika.GetType("Fika.Core.Main.ClientClasses.ClientQuestController")
                ?.GetMethod("FinishQuest", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (target == null) { Plugin.Log.LogWarning("[quest] Fika is installed but ClientQuestController.FinishQuest was not found (different version?); in co-op, teammates may see reward items of quests finished in raid that were not actually handed out"); return; }
            var self = typeof(Finish);
            new Harmony("com.sora.visitapi").Patch(target, prefix: new HarmonyMethod(self, "Prefix"), finalizer: new HarmonyMethod(self, "Finalizer"));
        }
        catch (Exception e) { Plugin.Log.LogWarning("[quest] hooking Fika's in-raid quest finish failed (single-player unaffected): " + e.Message); }
    }

    static void Enter(Quest quest)
    {
        if (_depth++ > 0) return;
        try
        {
            if (quest?.Rewards == null || !QuestFlags.Pack(quest.Id) || Narrating.Now) return;
            if (!Singleton<GameWorld>.Instantiated || Raid.IsHideout(Singleton<GameWorld>.Instance.LocationId)) return;
            if (!quest.Rewards.TryGetValue(EQuestStatus.Success, out var full) || full == null) return;
            var kept = full.Where(r => r.type != ERewardType.Item && r.type != ERewardType.Experience && r.type != ERewardType.TraderStanding).ToList();
            if (kept.Count == full.Count) return;
            quest.Rewards[EQuestStatus.Success] = kept;
            _quest = quest; _full = full;
        }
        catch (Exception e) { Plugin.Log.LogWarning($"[quest] could not hold back in-raid rewards of {quest?.Id} (they may arrive twice: in raid and by mail): {e.Message}"); }
    }

    static void Exit()
    {
        if (--_depth > 0) return;
        _depth = 0;
        if (_quest == null) return;
        try { _quest.Rewards[EQuestStatus.Success] = _full; }
        catch (Exception e) { Plugin.Log.LogWarning($"[quest] could not restore the reward list of {_quest.Id}: {e.Message}"); }
        _quest = null; _full = null;
    }
}
