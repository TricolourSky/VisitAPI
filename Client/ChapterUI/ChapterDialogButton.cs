using System.Linq;
using EFT;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;
using VisitAPI.Dialog;
using VisitAPI.Native;

namespace VisitAPI.ChapterUI
{
    /// <summary>目标行的「去找 X」按钮：哪个商人的 .dlg 有接/交/上交这条任务的选项，点了开对话（复用 DialogOpener）。
    /// <para>⚠️ 1.1 还有「去现场」「电台」两个提示按钮，借的是 0.16 **不存在**的邀请系统槽位，激活即 NRE 且把整页带崩，
    /// 旧 DEV_NOTES #86 白纸黑字「别再加回来」（G15 判不做的依据）——一律只藏不激活，连藏都包着。</para></summary>
    public static class ChapterDialogButton
    {
        /// 可接的任务找有 `accept:` 的对话；进行中/可交的找有 `complete:`/`handover:` 的。
        /// B11：多个 .dlg 都接同一任务时按商人 id 定序取第一个——回回一样，不再看目录枚举心情。
        public static DialogTree TraderFor(Quest quest)
        {
            var id = quest?.Id; if (id == null) return null;
            var accept = quest.QuestStatus == EQuestStatus.AvailableForStart;
            return DialogFiles.All().OrderBy(t => t.TraderId, System.StringComparer.Ordinal)
                .FirstOrDefault(t => t.Nodes.Values.SelectMany(n => n.Options).Any(o => accept ? o.AcceptIds.Contains(id) : o.CompleteIds.Contains(id) || o.HandoverId == id));
        }

        public static string Label(DialogTree tree) => Loc.Pick("去找 " + (tree.DisplayName ?? tree.TraderId), "VISIT " + (tree.DisplayName ?? tree.TraderId));

        public static void Bind(MainQuestTaskView row, Quest quest, QuestController quests)
        {
            var c = row._dialogButtonsContainer; if (c == null) return;
            var active = quest.QuestStatus == EQuestStatus.Started || quest.QuestStatus == EQuestStatus.AvailableForFinish;
            var tree = active ? TraderFor(quest) : null;
            var lobby = tree != null && !Raid.Now && c._visitTraderButton != null;
            c.gameObject.SetActive(lobby);
            // 这批 1.1 零件序列化在 0.16 下对不上，第一次激活可能 NRE；异常顺着 FillList 抛出去会把后面填日记的活废掉，所以包起来
            if (c._visitTraderButton != null)
            {
                try
                {
                    c._visitTraderButton.gameObject.SetActive(lobby);
                    if (lobby) { c._visitTraderButton.SetRawText(Label(tree), 14); c._visitTraderButton.OnClick.RemoveAllListeners(); c._visitTraderButton.OnClick.AddListener(() => Open(tree, ChapterTab.Profile, quests, ChapterTab.Inventory)); }
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning("[chapter] 「去找商人」按钮激活失败，已藏起来：" + e.Message);
                    try { c._visitTraderButton.gameObject.SetActive(false); } catch { }
                }
            }
            Hide(c._visitOnLocationButton);
            Hide(c._radioButton);
        }

        static void Hide(DefaultUIButton b)
        {
            if (b == null || !b.gameObject.activeSelf) return;
            try { b.gameObject.SetActive(false); } catch { }
        }

        public static void Open(DialogTree tree, Profile profile, QuestController quests, InventoryController inventory)
        {
            if (DialogScreenTracker.Open) return;
            if (profile == null || quests == null || inventory == null) { Plugin.Log.LogWarning("[chapter/dialog] no profile/quests/inventory to open with"); return; }
            if (!DialogOpener.TryOpen(tree, profile, quests, inventory, null, out var err)) Plugin.Log.LogWarning("[chapter/dialog] open failed: " + err);
        }
    }

    /// <summary>任务 JSON 标 `visitapi.dialogOnly` 的任务，接/交只能走对话：原生任务列表里的「接受/完成」按钮换成「去找 X」，
    /// 点了直接开那位商人的对话；战局里、或 .dlg 里没有对应选项时把按钮藏掉，免得玩家绕过剧情一键接交。DEV_NOTES #71。</summary>
    [HarmonyPatch(typeof(QuestView), nameof(QuestView.ShowButtonBlock))]
    public static class DialogOnlyButton
    {
        // 阶段四单点隔离：ShowButtonBlock 的调用方在填任务列表，这里炸了只损失按钮改写
        static void Postfix(QuestView __instance)
        {
            try { Rewire(__instance); }
            catch (System.Exception e) { Plugin.Log.LogWarning("[chapter/dialog] 按钮改写失败（任务列表不受影响）: " + e.Message); }
        }

        static void Rewire(QuestView __instance)
        {
            var t = Traverse.Create(__instance);
            var quest = t.Field("_quest").GetValue<Quest>();
            if (quest == null || !QuestFlags.DialogOnly(quest.Id)) return;
            if (quest.QuestStatus != EQuestStatus.AvailableForStart && quest.QuestStatus != EQuestStatus.AvailableForFinish) return;
            var button = __instance._button; button.OnClick.RemoveAllListeners();
            var tree = ChapterDialogButton.TraderFor(quest);
            if (tree == null || Raid.Now) { button.gameObject.SetActive(false); return; }
            button.SetRawText(ChapterDialogButton.Label(tree), 24);
            var profile = t.Field("_backendSession").GetValue<IEftSession>()?.Profile;
            var quests = t.Field("_questController").GetValue<QuestController>();
            var inventory = t.Field("_inventoryController").GetValue<InventoryController>();
            button.OnClick.AddListener(() => ChapterDialogButton.Open(tree, profile, quests, inventory));
        }
    }
}
