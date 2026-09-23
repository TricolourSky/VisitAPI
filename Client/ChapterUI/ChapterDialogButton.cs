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
    public static class ChapterDialogButton
    {
        public static DialogTree TraderFor(Quest quest)
        {
            var id = quest?.Id; if (id == null) return null;
            var accept = quest.QuestStatus == EQuestStatus.AvailableForStart;
            return DialogFiles.All().OrderBy(t => t.TraderId, System.StringComparer.Ordinal)
                .FirstOrDefault(t => t.Nodes.Values.SelectMany(n => n.Options).Any(o => accept ? o.AcceptIds.Contains(id) : o.CompleteIds.Contains(id) || o.HandoverId == id));
        }

        /// <summary>名字按玩家语言取（trader: 行下面的译文行，DialogLangs），没译文用 trader: 里写的那个。</summary>
        public static string Label(DialogTree tree)
        {
            var name = DialogLangs.Pick(tree.NameTr, Loc.Code, tree.DisplayName ?? tree.TraderId);
            return Loc.Pick("去找 " + name, "VISIT " + name);
        }

        public static string TalkText(Condition cond)
        {
            if (cond == null) return null;
            var key = cond.id + " talk"; var s = key.Localized();
            return string.IsNullOrEmpty(s) || s == key ? null : s;
        }
        static bool AnyTalkText(Quest quest) =>
            quest.Template?.Conditions != null && quest.Template.Conditions.TryGetValue(EQuestStatus.AvailableForFinish, out var cc) && cc.Any(c => TalkText(c) != null);

        public static void Bind(MainQuestTaskView row, Quest quest, QuestController quests, Condition cond = null)
        {
            var c = row._dialogButtonsContainer; if (c == null) return;
            var active = quest.QuestStatus == EQuestStatus.Started || quest.QuestStatus == EQuestStatus.AvailableForFinish;
            var tree = active ? TraderFor(quest) : null;
            var any = cond != null && AnyTalkText(quest);
            var custom = any ? TalkText(cond) : null;
            var lobby = tree != null && !Raid.Now && c._visitTraderButton != null && (!any || custom != null);
            c.gameObject.SetActive(lobby);
            if (c._visitTraderButton != null)
            {
                try
                {
                    c._visitTraderButton.gameObject.SetActive(lobby);
                    if (lobby) { c._visitTraderButton.SetRawText(custom ?? Label(tree), 14); c._visitTraderButton.OnClick.RemoveAllListeners(); c._visitTraderButton.OnClick.AddListener(() => Open(tree, ChapterTab.Profile, quests, ChapterTab.Inventory)); }
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

    [HarmonyPatch(typeof(QuestView), nameof(QuestView.ShowButtonBlock))]
    public static class DialogOnlyButton
    {
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
