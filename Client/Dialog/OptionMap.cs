using EFT;
using EFT.Dialogs;
using VisitAPI.Dialog;

namespace VisitAPI.Native;

public static class OptionMap
{
    public static MongoID Entry(DialogTree t, string nodeName) =>
        t.Nodes.TryGetValue(nodeName, out var n) && n.Narration.Count > 0 && n.NpcSlot > 0
            ? DialogTemplateBuilder.Id(t.TraderId, nodeName + "#nar0")
            : DialogTemplateBuilder.Id(t.TraderId, nodeName + "#npc");

    public static DialogAction Act(DialogTree t, string entry, string target)
    {
        // 10-16 SORA（通话被 Abyss 劫持）：`商人id:节点` 跳到别家剧本的节点。那家剧本由 DialogTemplateBuilder.LinkExternal 在开对话时一并注册，
        // 这里只是换一个对话 id；名字 / 头像由 SpeakerSwitch 跟着换。剧本或节点不在 → 当关闭对话，记一条警告
        if (DialogParser.IsExternal(target, out var traderId, out var node))
        {
            var other = traderId == t.TraderId ? t : DialogTemplateBuilder.Linked(traderId);
            if (other != null && other.Nodes.ContainsKey(node)) return new DialogSwitchDialogAction(Entry(other, node));
            Plugin.Log.LogWarning($"[dialog] {t.TraderId}: jump target '{target}' not found (no .dlg for that trader, or no such node), the option closes the dialogue instead");
            return new DialogQuitAction();
        }
        return target switch
        {
            null or "@close" or "@leave" or "@trade" or "@services" or "@tasks" => new DialogQuitAction(),
            "@start" => new DialogSwitchDialogAction(Entry(t, entry)),
            "@visit" => RetailDialogs.TryGetEntry(t.TraderId, out var visit) ? new DialogSwitchDialogAction(visit) : (DialogAction)new DialogQuitAction(),
            _ => t.Nodes.ContainsKey(target) ? new DialogSwitchDialogAction(Entry(t, target)) : (DialogAction)new DialogQuitAction(),
        };
    }

    public static DialogLineTemplate.EDialogLineIconType Icon(DialogOption o) =>
        o.HandoverId != null ? DialogLineTemplate.EDialogLineIconType.OpenPalm :
        o.CompleteId != null ? DialogLineTemplate.EDialogLineIconType.CheckMark :
        o.AcceptId != null ? DialogLineTemplate.EDialogLineIconType.QuestIcon :
        o.Target switch
        {
            null or "@close" or "@leave" => DialogLineTemplate.EDialogLineIconType.QuitIcon,
            "@trade" or "@services" => DialogLineTemplate.EDialogLineIconType.ShoppingCart,
            "@tasks" => DialogLineTemplate.EDialogLineIconType.QuestIcon,
            "@visit" => DialogLineTemplate.EDialogLineIconType.DialogBubble,
            _ => DialogLineTemplate.EDialogLineIconType.QuestionMark,
        };
}
