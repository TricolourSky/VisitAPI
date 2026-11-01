using System;
using System.Collections.Generic;
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
using SPTarkov.Server.Core.Models.Eft.Quests;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace VisitAPI.Server;

/// <summary>10-15（SORA 章节二「站在深渊一边」：选了 Abyss 就把 SORA 商人锁回去）：任务上写 `visitapi.lockTraders: ["<商人id>", …]`，
/// 任务 Success 时把档案里这些商人 `TradersInfo[id].Unlocked` 置回 false——就是 `unlockTraderOnReady`（原生 TraderUnlock 奖励）的反向，原生没有「锁商人」这种奖励类型。
/// 挂在 QuestHelper.CompleteQuest 后面：大厅里交任务、.dlg 对话里交、战局内交的结算补发（StoryRaidRewards 也是调它）三条路都经过这里。
/// 幂等：已经锁着的不动；没有开服自愈——这个字段 10-15 才有，没有历史坏档。</summary>
[Injectable(typePriority: OnLoadOrder.PostLoad + 21)]
public class QuestTraderLock(TemplateTable templates, ISptLogger<QuestTraderLock> log) : IOnLoad
{
    static TemplateTable _templates;
    static ISptLogger<QuestTraderLock> _log;

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        _templates = templates;
        _log = log;
        Patches.Arm(new CompleteQuestPatch(), log, "quest trader-lock (visitapi.lockTraders)");
        return Task.CompletedTask;
    }

    /// 任务 `visitapi.lockTraders` 里写的商人 id（只认 24 位十六进制）；没写就空
    internal static IEnumerable<string> LockTargets(MongoId questId)
    {
        if (_templates?.Quests == null || !_templates.Quests.TryGetValue(questId, out var quest) || quest?.ExtensionData == null) yield break;
        foreach (var id in Ext.StrList(Ext.Visit(quest.ExtensionData), "lockTraders"))
            if (id is { Length: 24 } && MongoId.IsValidMongoId(id)) yield return id;
    }

    public class CompleteQuestPatch : AbstractPatch
    {
        protected override MethodBase GetTargetMethod() =>
            typeof(QuestHelper).GetMethod("CompleteQuest", BindingFlags.Instance | BindingFlags.Public);

        /// 参数名与目标方法一致（pmcData / request / sessionId），补丁框架按名字喂
        [PatchPostfix]
        public static void Postfix(PmcData pmcData, CompleteQuestRequestData request, MongoId sessionId)
        {
            try
            {
                if (pmcData?.TradersInfo == null || request == null) return;
                foreach (var target in LockTargets(request.QuestId))
                    if (pmcData.TradersInfo.TryGetValue(new MongoId(target), out var info) && info?.Unlocked == true) info.Unlocked = false;
            }
            catch (Exception e) { _log?.Error($"[VisitAPI] trader lock after quest {request?.QuestId} failed (quest completion unaffected): {e.Message}"); }
        }
    }
}
