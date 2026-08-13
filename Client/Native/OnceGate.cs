using EFT.Dialogs;
using VisitAPI.Dialog;

namespace VisitAPI.Native;

public class OnceGate : DialogCondition
{
    readonly DialogStateStore _store;
    readonly string _profileId, _node;
    readonly int _option;

    public OnceGate(DialogStateStore store, string profileId, string node, int option)
    {
        _store = store; _profileId = profileId; _node = node; _option = option;
    }

    // 原生枚举没有自定义槽位, 借用 QuestStatus 通道进条件组(原生只按 Test() 结果消费, 不按 Type 分发)
    public override EDialogConditionType Type => EDialogConditionType.QuestStatus;

    public override bool Test(IDialogContext context) => !_store.OnceUsed(_profileId, _node, _option);
}
