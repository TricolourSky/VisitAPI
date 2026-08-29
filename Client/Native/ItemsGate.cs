using System.Linq;
using EFT.Dialogs;
using EFT.Quests;

namespace VisitAPI.Native;

/// <summary>`ifitems`：**背包里有东西可交**时这个选项才显示。
/// <para>宽松口径——有**一件**能交的就算数，不要求凑齐剩余数量；不然"先交 3 个、剩下 2 个下次再交"就做不了了。
/// 用的是引擎自己的 <c>GetItemsForCondition</c>（和真正上交时 HandoverService 走的是同一条路），
/// 所以"界面上看得到"和"点进去有东西选"永远一致。</para></summary>
public class ItemsGate : DialogCondition
{
    readonly QuestController _quests;
    readonly string _questId;

    public ItemsGate(QuestController quests, string questId) { _quests = quests; _questId = questId; }

    public override EDialogConditionType Type => EDialogConditionType.QuestStatus;

    public override bool Test(IDialogContext context)
    {
        var quest = _quests?.Quests?.GetConditional(_questId);
        if (quest?.ProgressCheckers == null) return false;
        var cond = quest.ProgressCheckers.Keys.OfType<ConditionItem>().FirstOrDefault(c => !quest.IsConditionDone(c));
        if (cond == null) return false;                       // 该交的都交完了：没东西可交
        var items = _quests.GetItemsForCondition(cond);
        return items != null && items.Length > 0;
    }
}
