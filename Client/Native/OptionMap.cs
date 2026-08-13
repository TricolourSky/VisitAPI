using EFT;
using EFT.Dialogs;
using VisitAPI.Dialog;

namespace VisitAPI.Native;

public static class OptionMap
{
    public static MongoID Entry(DialogTree t, string nodeName) =>
        t.Nodes.TryGetValue(nodeName, out var n) && n.Narration.Count > 0
            ? DialogTemplateBuilder.Id(t.TraderId, nodeName + "#nar0")
            : DialogTemplateBuilder.Id(t.TraderId, nodeName + "#npc");

    public static DialogAction Act(DialogTree t, string entry, string target) => target switch
    {
        null or "@close" or "@leave" or "@trade" or "@services" or "@tasks" => new DialogQuitAction(),
        "@start" => new DialogSwitchDialogAction(Entry(t, entry)),
        "@visit" => RetailDialogs.TryGetEntry(t.TraderId, out var visit) ? new DialogSwitchDialogAction(visit) : (DialogAction)new DialogQuitAction(),
        _ => t.Nodes.ContainsKey(target) ? new DialogSwitchDialogAction(Entry(t, target)) : (DialogAction)new DialogQuitAction(),
    };

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
