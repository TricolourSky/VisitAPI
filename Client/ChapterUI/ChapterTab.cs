using EFT;
using EFT.InventoryLogic;
using EFT.Notes;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using VisitAPI.ChapterUI;

namespace VisitAPI.Native;

[HarmonyPatch(typeof(TasksScreen), "Awake")]
public static class ChapterTab
{
    static Toggle _story;
    public static Profile Profile; public static InventoryController Inventory; public static QuestController Quests;

    const float TabRowLift = 82f;

    static void Postfix(TasksScreen __instance)
    {
        try { Inject(__instance); }
        catch (System.Exception e) { Plugin.Log.LogError("[chapter] 剧情页注入失败（任务屏本体不受影响）: " + e); }
    }

    static void Inject(TasksScreen screen)
    {
        var daily = screen._dailyQuestsToggleSpawner; var regular = screen._defaultQuestsToggleSpawner;
        var tabRow = (RectTransform)daily.transform.parent;
        var nativePart = (RectTransform)screen._tasksPanel.transform.parent;
        var spawned = daily.SpawnedObject != null ? daily.SpawnedObject.GetComponent<UISpawnableToggle>() : null;
        var caption = spawned != null ? spawned._headerLabel : null;
        var part = ChapterBundle.Instantiate("TasksPart", nativePart.parent, caption);
        if (part == null) return;
        part.name = "VisitAPI.TasksPart";
        var rt = (RectTransform)part.transform; rt.SetSiblingIndex(nativePart.GetSiblingIndex() + 1);
        rt.anchorMin = nativePart.anchorMin; rt.anchorMax = nativePart.anchorMax; rt.pivot = nativePart.pivot;
        rt.offsetMin = nativePart.offsetMin; rt.offsetMax = new Vector2(nativePart.offsetMax.x, nativePart.offsetMax.y + TabRowLift);
        var slot = part.transform.Find("SideQuestsPanel"); if (slot != null) slot.gameObject.SetActive(false);
        var desc = nativePart.Find("Description"); if (desc != null) desc.gameObject.SetActive(false);
        var group = part.transform.Find("QuestTypeGroup"); if (group != null) group.SetSiblingIndex(1);
        Dock(screen._tasksPanel.transform, part.transform);
        nativePart.gameObject.SetActive(false); tabRow.gameObject.SetActive(false);

        var toggles = group != null ? group.GetComponent<ToggleGroup>() : null;
        var story = Wire(group, "MainQuestToggleSpawner/MainQuestToggle", toggles, StoryCaption(), daily._headerFontSize);
        var side = Wire(group, "RegularQuestToggleSpawner/RegularQuestToggle", toggles, regular._headerCaption, regular._headerFontSize);
        var ops = Wire(group, "DailyQuestsToggleSpawner/DailyQuestsToggle", toggles, daily._headerCaption, daily._headerFontSize);
        var pve = group != null ? group.Find("MainQuestToggleSpawner/PvEBlockTooltip") : null; if (pve != null) pve.gameObject.SetActive(false);
        var panelT = part.transform.Find("MainQuestPanel"); var panel = panelT != null ? panelT.gameObject : null; if (panel != null) panel.SetActive(false);
        if (side != null) side.onValueChanged.AddListener(on => { if (on && regular.SpawnedObject != null) regular.SpawnedObject.isOn = true; });
        if (ops != null) ops.onValueChanged.AddListener(on => { if (on && daily.SpawnedObject != null) daily.SpawnedObject.isOn = true; });
        if (story != null) story.onValueChanged.AddListener(on =>
        {
            screen._tasksPanel.gameObject.SetActive(!on);
            if (panel == null) return;
            panel.SetActive(on);
            var view = on ? panel.GetComponent<MainQuestTabView>() : null; if (view != null) view.Show(Quests);
        });
        _story = story;
        Plugin.Log.LogDebug("[chapter] 1.1 TasksPart injected");
    }

    static string StoryCaption()
    {
        var key = "UI/MainQuests/TabName"; var text = key.Localized();
        return text == key ? Loc.Pick("剧情", "STORY") : text;
    }

    static void Dock(Transform t, Transform host)
    {
        t.SetParent(host, false); t.SetSiblingIndex(2);
        (t.GetComponent<LayoutElement>() ?? t.gameObject.AddComponent<LayoutElement>()).ignoreLayout = true;
        var r = ((RectTransform)t).Stretch(); r.pivot = new Vector2(0.5f, 0.5f);
        r.offsetMax = new Vector2(0, -50);
    }

    static Toggle Wire(Transform bar, string path, ToggleGroup group, string captionKey, int fontSize)
    {
        var t = bar != null ? bar.Find(path) : null; if (t == null) { Plugin.Log.LogWarning("[chapter] tab missing: " + path); return null; }
        t.gameObject.SetActive(true);
        var st = t.GetComponent<UISpawnableToggle>(); if (st == null || st.Toggle == null) { Plugin.Log.LogWarning("[chapter] tab not bound: " + path); return null; }
        st.Init(group); st.InitSpawnableButton(captionKey, fontSize > 0 ? fontSize : 20, null, null);
        st.Toggle.SetIsOnWithoutNotify(false);
        return st.Toggle;
    }

    [HarmonyPatch(typeof(TasksScreen), nameof(TasksScreen.Show), typeof(InventoryController), typeof(QuestController), typeof(IEftSession), typeof(NotesManager), typeof(bool))]
    public static class ShowPatch
    {
        static void Postfix(InventoryController inventoryController, QuestController questController, IEftSession session)
        {
            try
            {
                Profile = session?.Profile; Inventory = inventoryController; Quests = questController;
                ReadState.Sync();
                if (_story != null) { _story.isOn = false; _story.isOn = true; }
            }
            catch (System.Exception e) { Plugin.Log.LogError("[chapter] 剧情页默认落位失败: " + e.Message); }
        }
    }
}
