using EFT;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;
using VisitAPI.Dialog;
using VisitAPI.Native;

namespace VisitAPI.ChapterUI
{
    public static class ChapterDialogButton
    {
        /// 10-18 SORA：只认点访问走得到的接 / 交选项，只挂在战局 / 藏身处触发点后面的不算（见 VisitReach）
        public static DialogTree TraderFor(Quest quest)
        {
            var id = quest?.Id; if (id == null) return null;
            return VisitReach.TraderFor(id, quest.QuestStatus == EQuestStatus.AvailableForStart);
        }

        public static string TalkText(Condition cond) => cond == null ? null : Loc.Key(cond.id + " talk");

        public static void Bind(MainQuestTaskView row, Quest quest, QuestController quests, Condition cond)
        {
            var c = row._dialogButtonsContainer; if (c == null) return;
            // 10-01 SORA：默认的「去找 X」行按钮退役（指路改走金色电话 + 金色访问）；只有作者专门写了 talk 文案的行才保留按钮。
            // 10-24（旧 B22）：先看有没有 talk 文案、是不是大厅，都对了才去翻剧本找商人
            var custom = TalkText(cond);
            var tree = custom != null && !Raid.Now && ChapterStates.Running(quest.QuestStatus) ? TraderFor(quest) : null;
            var lobby = tree != null && c._visitTraderButton != null;
            c.gameObject.SetActive(lobby);
            if (c._visitTraderButton != null)
            {
                try
                {
                    c._visitTraderButton.gameObject.SetActive(lobby);
                    if (lobby) { c._visitTraderButton.SetRawText(custom, 14); c._visitTraderButton.OnClick.RemoveAllListeners(); c._visitTraderButton.OnClick.AddListener(() => Open(tree, ChapterTab.Profile, quests, ChapterTab.Inventory)); }
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning("[chapter] Failed to activate the \"visit trader\" button, hid it: " + e.Message);
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

    [HarmonyPatch(typeof(QuestView), nameof(QuestView.ShowButtonBlock))]
    public static class DialogOnlyButton
    {
        /// ____quest = QuestView 的私有字段 _quest，由 Harmony 注入（10-24，旧 B15）。
        /// Harmony 的规矩是「三个下划线 + 字段原名」，字段本身带一个下划线，所以是四个——10-24 写成三个，补丁整个没装上（10-25 日志 No such field，当天修）
        static void Postfix(QuestView __instance, object ____quest)
        {
            try { Rewire(__instance, ____quest as Quest); }
            catch (System.Exception e) { Plugin.Log.LogWarning("[chapter/dialog] Button rewrite failed (the quest list is unaffected): " + e.Message); }
        }

        static void Rewire(QuestView view, Quest quest)
        {
            if (quest == null || !QuestFlags.DialogOnly(quest.Id)) return;
            if (quest.QuestStatus != EQuestStatus.AvailableForStart && quest.QuestStatus != EQuestStatus.AvailableForFinish) return;
            // 10-01 SORA：任务页不再放「去找 X」按钮，直接藏掉——接/交只能走对话，入口由商人头上的金色电话 + 金色访问指路。
            // 原生按钮的监听也要摘干净，不然藏住的按钮还挂着「接受/完成」的旧动作
            var button = view._button;
            button.OnClick.RemoveAllListeners();
            button.gameObject.SetActive(false);
        }
    }
}
