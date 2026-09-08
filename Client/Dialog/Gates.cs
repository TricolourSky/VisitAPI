using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.Dialogs;
using EFT.Quests;
using VisitAPI.Dialog;

namespace VisitAPI.Native;

// ══ 选项门控（DialogCondition 一族）与门控/动作的装配 ══
// 原生枚举没有自定义槽位, 全部借用 QuestStatus 通道进条件组(原生只按 Test() 结果消费, 不按 Type 分发)。

public class QuestGate : DialogCondition
{
    readonly QuestController _quests;
    readonly string _questId;
    readonly List<int> _statuses;
    readonly bool _not;

    public QuestGate(QuestController quests, string questId, List<int> statuses, bool not)
    { _quests = quests; _questId = questId; _statuses = statuses; _not = not; }

    public override EDialogConditionType Type => EDialogConditionType.QuestStatus;

    public override bool Test(IDialogContext context)
    {
        var quest = _quests?.Quests?.GetConditional(_questId);
        var status = quest != null ? (int)quest.QuestStatus : 0;
        return _not ? !_statuses.Contains(status) : _statuses.Contains(status);
    }
}

/// <summary>`ifitems`：背包里有东西可交时选项才显示。宽松口径——有一件能交的就算数（"先交 3 个剩 2 个"才做得了）。
/// 用引擎自己的 GetItemsForCondition，和真正上交同一条路，"看得到"和"点进去有东西选"永远一致。</summary>
public class ItemsGate : DialogCondition
{
    readonly QuestController _quests;
    readonly string _questId;

    public ItemsGate(QuestController quests, string questId) { _quests = quests; _questId = questId; }

    public override EDialogConditionType Type => EDialogConditionType.QuestStatus;

    public override bool Test(IDialogContext context)
    {
        var cond = QuestGates.PendingItems(_quests?.Quests?.GetConditional(_questId));
        if (cond == null) return false;   // 该交的都交完了：没东西可交
        var items = _quests.GetItemsForCondition(cond);
        return items != null && items.Length > 0;
    }
}

public class OnceGate : DialogCondition
{
    readonly DialogStateStore _store;
    readonly string _profileId, _node;
    readonly int _option;

    public OnceGate(DialogStateStore store, string profileId, string node, int option)
    { _store = store; _profileId = profileId; _node = node; _option = option; }

    public override EDialogConditionType Type => EDialogConditionType.QuestStatus;

    public override bool Test(IDialogContext context) => !_store.OnceUsed(_profileId, _node, _option);
}

public static class QuestGates
{
    /// <summary>这条任务还没交完的第一个上交类条件（ifitems 门控和 handover 上交窗共用，「看得到」和「点进去有东西选」永远一致）。</summary>
    public static ConditionItem PendingItems(Quest quest) =>
        quest?.ProgressCheckers?.Keys.Where(quest.CheckVisibilityStatus).OfType<ConditionItem>().FirstOrDefault(c => !quest.IsConditionDone(c));

    public static DialogAction[] Actions(DialogOption o, DialogAction main)
    {
        var acts = new List<DialogAction>();
        // accept:/complete: 可以一次带多个任务，一个动作一条
        foreach (var id in o.AcceptIds) acts.Add(new DialogAcceptQuestAction(id));
        foreach (var id in o.CompleteIds) acts.Add(new DialogFinishQuestAction { QuestId = id });
        // 记号先写再跳转：下一屏的 ifvar: 门控读的就是这一笔
        if (o.SetVarName != null) acts.Add(new DialogSetVariableAction(new DialogSetVariableAction.SaveStateData(Vars.Id(o.SetVarName), o.SetVarValue, DialogLineTemplate.ESaveStateType.Profile)));
        if (main != null) acts.Add(main);
        return acts.Count > 0 ? acts.ToArray() : null;
    }

    public static DialogMainConditionGroup Trigger(DialogOption o, QuestController quests, DialogCondition extra = null)
    {
        var conds = new List<DialogCondition>();
        if (o.IfQuestId != null) conds.Add(new QuestGate(quests, o.IfQuestId, o.IfStatuses, false));
        if (o.IfNotQuestId != null) conds.Add(new QuestGate(quests, o.IfNotQuestId, o.IfNotStatuses, true));
        if (o.IfVarName != null) conds.Add(new VariableValueCondition(Vars.Id(o.IfVarName), o.IfVarValue));
        // `ifitems` 是**追加**的一条，不参与"没写任何条件就自动补一条"的判断。
        // 只认 `ifitems: 任务` 明写的、或同选项 handover: 的那条。**不能拿 complete: 兜底**——
        // 任务能交时物品条件早就做完了，ItemsGate 恒 false，那个选项会永远不出现（最难查的一类症状）。
        var itemsFor = o.IfItems ? (o.IfItemsId ?? o.HandoverId) : null;
        if (conds.Count == 0 && !o.Always)
        {
            // 多任务时每个都要到位（子组内条件是"且"）
            if (o.AcceptIds.Count > 0) foreach (var id in o.AcceptIds) conds.Add(new QuestGate(quests, id, new List<int> { (int)EQuestStatus.AvailableForStart }, false));
            else if (o.HandoverId != null) conds.Add(new QuestGate(quests, o.HandoverId, new List<int> { (int)EQuestStatus.Started }, false));
            else if (o.CompleteIds.Count > 0) foreach (var id in o.CompleteIds) conds.Add(new QuestGate(quests, id, new List<int> { (int)EQuestStatus.AvailableForFinish }, false));
        }
        if (itemsFor != null) conds.Add(new ItemsGate(quests, itemsFor));
        if (extra != null) conds.Add(extra);
        return conds.Count == 0 ? null : new DialogMainConditionGroup(new[] { new DialogConditionSubGroup(conds) });
    }
}
