using EFT;
using EFT.InventoryLogic;
using EFT.Notes;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VisitAPI.ChapterUI;

namespace VisitAPI.Native;

/// <summary>
/// 任务屏左半边换成 1.1 的 TasksPart 当宿主（页签栏 + 底图 + 章节屏，布局由 1.1 自己管），
/// 0.16 的原生任务列表 _tasksPanel 和 Description 搬进 1.1 的 SideQuestsPanel 槽位——逻辑还是 0.16 的，只换了父节点。
/// 原生页签行/原生 TasksPart 藏起来不删（TasksScreen 还引用 spawner）；1.1 页签点击把 isOn 转发给原生 toggle。DEV_NOTES #70。
/// </summary>
[HarmonyPatch(typeof(TasksScreen), "Awake")]
public static class ChapterTab
{
    static Toggle _story;
    /// 「去找商人」按钮开对话要用的档案/背包（任务屏 Show 时截下来；QuestController 从 Show(quests) 传）
    public static Profile Profile; public static InventoryController Inventory; public static QuestController Quests;

    static void Postfix(TasksScreen __instance)
    {
        var daily = __instance._dailyQuestsToggleSpawner; var regular = __instance._defaultQuestsToggleSpawner;
        var tabRow = (RectTransform)daily.transform.parent;
        var nativePart = (RectTransform)__instance._tasksPanel.transform.parent;
        var spawned = daily.SpawnedObject != null ? daily.SpawnedObject.GetComponent<UISpawnableToggle>() : null;
        var caption = spawned != null ? spawned._headerLabel : null;   // 只当字体样板，缺了也能装
        var part = ChapterBundle.Instantiate("TasksPart", nativePart.parent, caption);
        if (part == null) return;
        part.name = "VisitAPI.TasksPart";
        var rt = (RectTransform)part.transform; rt.SetSiblingIndex(nativePart.GetSiblingIndex() + 1);
        rt.anchorMin = nativePart.anchorMin; rt.anchorMax = nativePart.anchorMax; rt.pivot = nativePart.pivot;
        rt.offsetMin = nativePart.offsetMin; rt.offsetMax = nativePart.offsetMax;
        rt.offsetMax = new Vector2(rt.offsetMax.x, rt.offsetMax.y + 82);   // 1.1 页签栏要紧贴屏幕大页签底边那根分隔线的下方（正式版如此）：+60 时栏顶在屏幕顶下 87px，目标 ~58px，差 22 单位 → +82；底图顺带盖住原生页签行留下的黑带
        // 1.1 TasksPart 是 VerticalLayoutGroup（Background / QuestTypeGroup / MainQuestPanel / SideQuestsPanel，从上到下）：页签栏强制排到 Background 之后；
        // 0.16 列表/描述搬进来后退出布局、锚定到页签栏（50px）下方铺满
        var slot = part.transform.Find("SideQuestsPanel"); if (slot != null) slot.gameObject.SetActive(false);
        // 0.16 的 Description（QuestDescription(Clone)）在原生里被列表盖着从不露面，任务展开走列表行内；搬家后列表底透明它就漏出来（红块）→ 永远关掉
        var desc = nativePart.Find("Description"); if (desc != null) desc.gameObject.SetActive(false);
        var group = part.transform.Find("QuestTypeGroup"); if (group != null) group.SetSiblingIndex(1);
        Dock(__instance._tasksPanel.transform, part.transform);
        nativePart.gameObject.SetActive(false); tabRow.gameObject.SetActive(false);

        var bar = group; var toggles = bar != null ? bar.GetComponent<ToggleGroup>() : null;
        var story = Wire(bar, "MainQuestToggleSpawner/MainQuestToggle", toggles, Loc.Pick("剧情", "STORY"), daily._headerFontSize);   // 直接给文案：locale 键只有中/英两语有
        var side = Wire(bar, "RegularQuestToggleSpawner/RegularQuestToggle", toggles, regular._headerCaption, regular._headerFontSize);
        var ops = Wire(bar, "DailyQuestsToggleSpawner/DailyQuestsToggle", toggles, daily._headerCaption, daily._headerFontSize);
        var pve = bar != null ? bar.Find("MainQuestToggleSpawner/PvEBlockTooltip") : null; if (pve != null) pve.gameObject.SetActive(false);
        var panelT = part.transform.Find("MainQuestPanel"); var panel = panelT != null ? panelT.gameObject : null; if (panel != null) panel.SetActive(false);
        if (side != null) side.onValueChanged.AddListener(on => { if (on) regular.SpawnedObject.isOn = true; });
        if (ops != null) ops.onValueChanged.AddListener(on => { if (on) daily.SpawnedObject.isOn = true; });
        if (story != null) story.onValueChanged.AddListener(on =>
        {
            __instance._tasksPanel.gameObject.SetActive(!on);
            if (panel == null) return;
            panel.SetActive(on);
            var view = on ? panel.GetComponent<MainQuestTabView>() : null; if (view != null) view.Show(Quests);
        });
        _story = story;
        Plugin.Log.LogDebug("[chapter] 1.1 TasksPart injected");
    }

    static void Dock(Transform t, Transform host)
    {
        t.SetParent(host, false); t.SetSiblingIndex(2);
        (t.GetComponent<LayoutElement>() ?? t.gameObject.AddComponent<LayoutElement>()).ignoreLayout = true;
        var r = (RectTransform)t; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.pivot = new Vector2(0.5f, 0.5f);
        r.offsetMin = Vector2.zero; r.offsetMax = new Vector2(0, -50);
    }

    // 1.1 场景里页签是 spawner 下面关着的模板；激活它、挂进 ToggleGroup、按原生 spawner 的文案键/字号初始化
    static Toggle Wire(Transform bar, string path, ToggleGroup group, string captionKey, int fontSize)
    {
        var t = bar != null ? bar.Find(path) : null; if (t == null) { Plugin.Log.LogWarning("[chapter] tab missing: " + path); return null; }
        t.gameObject.SetActive(true);
        var st = t.GetComponent<UISpawnableToggle>(); if (st == null || st.Toggle == null) { Plugin.Log.LogWarning("[chapter] tab not bound: " + path); return null; }
        st.Init(group); st.InitSpawnableButton(captionKey, fontSize > 0 ? fontSize : 20, null, null);
        st.Toggle.SetIsOnWithoutNotify(false);
        return st.Toggle;
    }

    // 正式版打开任务页默认落在「剧情」；0.16 的 Show 会把原生「支线」置 on，之后再把我们的「剧情」置 on（带通知：动画/面板切换都走一遍）
    [HarmonyPatch(typeof(TasksScreen), nameof(TasksScreen.Show), typeof(InventoryController), typeof(QuestController), typeof(IEftSession), typeof(NotesManager), typeof(bool))]
    static class ShowPatch
    {
        static void Postfix(InventoryController inventoryController, QuestController questController, IEftSession session)
        {
            Profile = session?.Profile; Inventory = inventoryController; Quests = questController;
            if (_story != null) { _story.isOn = false; _story.isOn = true; }
        }
    }
}
