using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Quest;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Quests;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.InRaid;

namespace VisitAPI.Server;

/// <summary>战局内完成的剧情任务，奖励会永久丢（10-01 用户坏档报告，机制对着 LocationLifecycleService 反编译核过）：
/// 客户端在战局里只改本地状态，SPT 结算只记「已完成」、不跑奖励管线——解锁商人这类只有服务端会发的奖励就没了，档就此卡死。
/// 修两层：
/// ① 结算补发：照 SPT 给灯塔商人的官方补法（LightkeeperQuestWorkaround，注释原话「run them through the servers CompleteQuest process」），
///    战局里新变成功的本模组任务补跑一遍 CompleteQuest（解锁商人 / 物品邮件 / 经验声望）。
///    10-01 晚更正：原先写的「服务端此前一分没发过，不存在重复」只对服务端独有的奖励成立——原生的局内交任务（ItemManipulator.FinishConditional）
///    会当场把物品塞进背包、给本地档案加经验声望，这里再发一遍就成了双份（SORA 实测：伐木场 10 万卢布 + 比特币各两份）。
///    现在由客户端 RaidRewardHold 在局内那一步什么都不发（任务清单靠 /visitapi/quest/flags 的 pack 标记对齐），奖励只走这里。
///    比官方版多盖一种边角：同一局里接了又交的任务——赛前档案里根本没有这条，SPT 自家的 preRaid 过滤会把它漏掉。
/// ② 开服自愈：历史坏档（任务已成功、解锁商人的奖励却没发）直接把商人解开；已经卡住的玩家不用删档。幂等，跑几遍结果一样。</summary>
[Injectable(typePriority: OnLoadOrder.PostLoad + 20)]
public class StoryRaidRewards(SaveServer saves, TemplateTable templates, QuestHelper questHelper, ISptLogger<StoryRaidRewards> log) : IOnLoad
{
    static QuestHelper _quests;
    static ISptLogger<StoryRaidRewards> _log;

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        _quests = questHelper;
        _log = log;
        var patch = new SettleRaidQuestsPatch();
        try
        {
            patch.Enable();
            if (!patch.IsActive) log.Error("[VisitAPI] raid-end quest reward settlement did NOT arm (IsActive=false after Enable)");
        }
        catch (Exception e) { log.Error("[VisitAPI] raid-end quest reward settlement failed to arm: " + e); }
        RepairProfiles();
        return Task.CompletedTask;
    }

    /// 开服自愈：本模组的任务已 Success、Success 奖励里写着解锁商人、档案里那位商人却还锁着 → 解开并写警告留痕
    void RepairProfiles()
    {
        try
        {
            foreach (var (sessionId, profile) in saves.GetProfiles())
            {
                var pmc = profile?.CharacterData?.PmcData;
                if (pmc?.Quests == null || pmc.TradersInfo == null) continue;
                foreach (var qs in pmc.Quests)
                {
                    if (qs?.Status != QuestStatusEnum.Success) continue;
                    var qid = qs.QId.ToString();
                    if (!QuestLoader.Loaded.Contains(qid)) continue;
                    foreach (var target in UnlockTargets(qs.QId))
                    {
                        if (!pmc.TradersInfo.TryGetValue(new MongoId(target), out var info) || info == null || info.Unlocked == true) continue;
                        info.Unlocked = true;
                        log.Warning($"[VisitAPI] Profile {sessionId}: quest {qid} succeeded but its trader-unlock reward ({target}) was never granted (in-raid completion lost it); unlocked the trader");
                    }
                }
            }
        }
        catch (Exception e) { log.Error("[VisitAPI] locked-trader profile repair failed (profiles untouched): " + e); }
    }

    IEnumerable<string> UnlockTargets(MongoId qid)
    {
        if (!templates.Quests.TryGetValue(qid, out var quest) || quest?.Rewards == null) yield break;
        if (!quest.Rewards.TryGetValue("Success", out var list) || list == null) yield break;
        foreach (var r in list)
            if (r?.Type == RewardType.TraderUnlock && r.Target?.Length == 24) yield return r.Target;
    }

    public class SettleRaidQuestsPatch : AbstractPatch
    {
        protected override MethodBase GetTargetMethod() =>
            typeof(LocationLifecycleService).GetMethod("LightkeeperQuestWorkaround", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        /// 参数名与目标方法一致（sessionId / postRaidQuests / preRaidQuests / pmcProfile），补丁框架按名字喂
        [PatchPostfix]
        public static void Postfix(MongoId sessionId, List<QuestStatus> postRaidQuests, List<QuestStatus> preRaidQuests, PmcData pmcProfile)
        {
            try
            {
                if (_quests == null || postRaidQuests == null) return;
                foreach (var post in postRaidQuests.Where(p => p?.Status == QuestStatusEnum.Success
                             && QuestLoader.Loaded.Contains(p.QId.ToString())
                             && !preRaidQuests.Any(pre => pre.QId == p.QId && pre.Status == QuestStatusEnum.Success)))
                {
                    try
                    {
                        _quests.CompleteQuest(pmcProfile, new CompleteQuestRequestData
                        {
                            Action = "CompleteQuest",
                            QuestId = post.QId,
                            RemoveExcessItems = false
                        }, sessionId);
                        _log?.Info($"[VisitAPI] Quest {post.QId} was finished inside the raid; ran the server reward pass at raid end so nothing is lost");
                    }
                    catch (Exception e) { _log?.Error($"[VisitAPI] raid-end reward pass for quest {post.QId} failed (its status is unaffected): {e.Message}"); }
                }
            }
            catch (Exception e) { _log?.Error("[VisitAPI] raid-end reward settlement failed (raid outcome unaffected): " + e.Message); }
        }
    }
}
