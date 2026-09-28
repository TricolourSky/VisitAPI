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
        catch (System.Exception e) { Plugin.Log.LogError("[chapter] Story tab injection failed (the tasks screen itself is unaffected): " + e); }
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
        Backdrop11(screen);
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
    }

    /// <summary>09-26（SORA：战局里剧情页背景是透明的）：1.1MCP 导出 1.1 的 InventoryScreen/Tasks Panel——任务页自己带一张全屏底 OverallBackground
    ///（QuestsTabMainBackground，纯色 #0B0B0B@0.757；锚点铺满、pos(0,-21.5)、size(0,-43)，只让出顶上 43 的页签行）和右上一条 WhiteBackground_Right（白 @0.039），
    /// 剧情页 TasksPart 的 Background（QuestsTabQuestListBackground，@0.80）叠在上面，合起来约 95% 不透明。我们只搬了 TasksPart，战局里底下直接是游戏画面（只剩 80%）。
    /// 照 1.1 在任务页上补这两层，放最底下；0.16 已有同名物体就不动</summary>
    static void Backdrop11(TasksScreen screen)
    {
        var root = screen.transform as RectTransform;
        if (root == null) return;
        // 09-26 实机日志：0.16 的任务页本来就有 OverallBackground，但用的是 main_part_gradient（上下渐变、部分透明）× 0.78——战局里透的就是它。
        // 有就改成 1.1 的样子（纯色、位置），没有才新建
        var overall = root.Find("OverallBackground") as RectTransform;
        if (overall == null) { overall = Layer(root, "OverallBackground", Overall11); overall.SetSiblingIndex(0); }
        else if (overall.GetComponent<Image>() is Image oi) { oi.sprite = null; oi.type = Image.Type.Simple; oi.color = Overall11; }
        overall.anchorMin = Vector2.zero; overall.anchorMax = Vector2.one; overall.pivot = new Vector2(0.5f, 0.5f);
        overall.anchoredPosition = new Vector2(0f, -21.5f); overall.sizeDelta = new Vector2(0f, -43f);
        if (root.Find("WhiteBackground_Right") == null)
        {
            var w = Layer(root, "WhiteBackground_Right", new Color(1f, 1f, 1f, 0.039f));
            w.anchorMin = new Vector2(0f, 1f); w.anchorMax = new Vector2(1f, 1f); w.pivot = new Vector2(0f, 1f);
            w.anchoredPosition = new Vector2(1294f, -43f); w.sizeDelta = new Vector2(-1369f, 77f);
            w.SetSiblingIndex(0);
        }
        // 1.1 的任务页没有左边那条白底
        var left = root.Find("WhiteBackground_Left");
        if (left != null && left.gameObject.activeSelf) left.gameObject.SetActive(false);
    }

    static readonly Color Overall11 = new Color(0.043f, 0.043f, 0.043f, 0.757f);

    static RectTransform Layer(RectTransform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color; img.raycastTarget = false;
        if (go.GetComponent<LayoutElement>() == null) go.AddComponent<LayoutElement>().ignoreLayout = true;
        return rt;
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
            catch (System.Exception e) { Plugin.Log.LogError("[chapter] Failed to select the story tab by default: " + e.Message); }
        }
    }
}
