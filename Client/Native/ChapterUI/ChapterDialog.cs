using System.Linq;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.UI;
using VisitAPI.Dialog;
using VisitAPI.Native;

namespace VisitAPI.ChapterUI
{
    /// <summary>目标行的三个小按钮（1.1 的 DialogButtonsContainer：VisitAtLobby / ReplyByRadio / VisitAtLocation），全从 .dlg 反推、零新语法：
    /// 「去找 X」= 哪个商人的 .dlg 有接/交/上交这条任务的选项（点了开对话，复用 DialogOpener）；
    /// 「去现场：地图」= `trigger: raid 地图 … if 任务=状态`；「去藏身处」= `trigger: hideout … if 任务=状态`（后两个只是提示、不可点——1.1 的也只是告诉你去哪）。DEV_NOTES #71/#75。</summary>
    public static class ChapterDialog
    {
        /// 可接的任务找有 `accept:` 的对话；进行中/可交的找有 `complete:`/`handover:` 的。靠触发点接的任务在菜单里没得找，返回 null
        public static DialogTree TraderFor(Quest quest)
        {
            var id = quest?.Id; if (id == null) return null;
            var accept = quest.QuestStatus == EQuestStatus.AvailableForStart;
            return DialogFiles.All().FirstOrDefault(t => t.Nodes.Values.SelectMany(n => n.Options).Any(o => accept ? o.AcceptIds.Contains(id) : o.CompleteIds.Contains(id) || o.HandoverId == id));
        }

        public static bool InRaid => Singleton<AbstractGame>.Instantiated && Singleton<AbstractGame>.Instance.InRaid;

        public static string Label(DialogTree tree) => Loc.Pick("去找 " + (tree.DisplayName ?? tree.TraderId), "VISIT " + (tree.DisplayName ?? tree.TraderId));

        /// 触发点反推：这条任务在当前状态下，哪张图 / 藏身处有它的触发点（只认带 `if 本任务=状态` 的）
        static (string map, bool hideout) Where(Quest quest)
        {
            string map = null; var hideout = false;
            foreach (var t in DialogFiles.All().SelectMany(t => t.Triggers))
                if (t.IfQuestId == quest.Id && t.IfStatuses.Contains((int)quest.QuestStatus)) { if (t.Kind == "raid") map = map ?? t.Place; else hideout = true; }
            return (map, hideout);
        }

        public static void Bind(MainQuestTaskView row, Quest quest, QuestController quests)
        {
            var c = row._dialogButtonsContainer; if (c == null) return;
            var active = quest.QuestStatus == EQuestStatus.Started || quest.QuestStatus == EQuestStatus.AvailableForFinish;
            var tree = active ? TraderFor(quest) : null;
            var (map, hideout) = active ? Where(quest) : (null as string, false);
            var lobby = tree != null && !InRaid && c._visitTraderButton != null;
            c.gameObject.SetActive(lobby || map != null || hideout);
            if (c._visitTraderButton != null)
            {
                c._visitTraderButton.gameObject.SetActive(lobby);
                if (lobby) { c._visitTraderButton.SetRawText(Label(tree), 14); c._visitTraderButton.OnClick.RemoveAllListeners(); c._visitTraderButton.OnClick.AddListener(() => Open(tree, ChapterTab.Profile, quests, ChapterTab.Inventory)); }
            }
            Hint(c._visitOnLocationButton, map != null ? Loc.Pick("去现场：", "GO TO: ") + map.Localized() : null);   // 地图 id 就是游戏文案键（Sandbox→中心区）
            Hint(c._radioButton, hideout ? Loc.Pick("去藏身处", "HIDEOUT") : null);
        }

        // 提示型按钮：亮着但不可点
        static void Hint(DefaultUIButton b, string text)
        {
            if (b == null) return;
            b.gameObject.SetActive(text != null);
            if (text != null) { b.SetRawText(text, 14); b.Interactable = false; }
        }

        public static void Open(DialogTree tree, Profile profile, QuestController quests, InventoryController inventory)
        {
            if (UnityEngine.Object.FindObjectOfType<TraderDialogScreen>() != null) return;
            if (profile == null || quests == null || inventory == null) { Plugin.Log.LogWarning("[chapter/dialog] no profile/quests/inventory to open with"); return; }
            if (!DialogOpener.TryOpen(tree, profile, quests, inventory, null, out var err)) Plugin.Log.LogWarning("[chapter/dialog] open failed: " + err);
        }
    }
}
