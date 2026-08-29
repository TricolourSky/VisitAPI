using EFT;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;
using VisitAPI.ChapterUI;

namespace VisitAPI.Native;

/// <summary>任务 JSON 标 `visitapi.dialogOnly` 的任务，接/交只能通过对话推进：原生任务列表里的「接受/完成」按钮换成「去找 X」，
/// 点了直接开那位商人的对话；战局里、或 .dlg 里没有对应选项（比如靠触发点接的）时把按钮藏掉，免得玩家绕过剧情一键接交。
/// 挂在 QuestView.ShowButtonBlock 之后。DEV_NOTES #71。</summary>
[HarmonyPatch(typeof(QuestView), nameof(QuestView.ShowButtonBlock))]
public static class DialogOnlyButton
{
    static void Postfix(QuestView __instance)
    {
        var t = Traverse.Create(__instance);
        var quest = t.Field("_quest").GetValue<Quest>();
        if (quest == null || !QuestFlags.DialogOnly(quest.Id)) return;
        if (quest.QuestStatus != EQuestStatus.AvailableForStart && quest.QuestStatus != EQuestStatus.AvailableForFinish) return;
        var button = __instance._button; button.OnClick.RemoveAllListeners();
        var tree = ChapterDialog.TraderFor(quest);
        if (tree == null || ChapterDialog.InRaid) { button.gameObject.SetActive(false); return; }
        button.SetRawText(ChapterDialog.Label(tree), 24);
        var profile = t.Field("_backendSession").GetValue<IEftSession>()?.Profile;
        var quests = t.Field("_questController").GetValue<QuestController>();
        var inventory = t.Field("_inventoryController").GetValue<InventoryController>();
        button.OnClick.AddListener(() => ChapterDialog.Open(tree, profile, quests, inventory));
    }
}
