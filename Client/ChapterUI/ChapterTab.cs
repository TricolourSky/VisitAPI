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

/// <summary>任务屏左半边换成 1.1 的 TasksPart 当宿主（页签栏 + 底图 + 章节屏，布局由 1.1 自己管），
/// 0.16 的原生任务列表搬进 1.1 的槽位——逻辑还是 0.16 的，只换父节点。原生页签行藏起来不删（TasksScreen 还引用 spawner）。
/// B8：整个 Postfix 包 try/catch——这里炸了只损失剧情页签，任务屏本体照常。DEV_NOTES #70。</summary>
[HarmonyPatch(typeof(TasksScreen), "Awake")]
public static class ChapterTab
{
    static Toggle _story;
    /// 「去找商人」按钮开对话要用的档案/背包（任务屏 Show 时截下来；QuestController 从 Show(quests) 传）
    public static Profile Profile; public static InventoryController Inventory; public static QuestController Quests;

    // 1.1 页签栏要紧贴屏幕大页签底边分隔线下方（正式版实测：+60 时差 22 单位 → +82，顺带盖住原生页签行的黑带）。
    // 实机量出来的魔数，没有可推导的来源，动它前先截图对照正式版。
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
        var caption = spawned != null ? spawned._headerLabel : null;   // 只当字体样板，缺了也能装
        var part = ChapterBundle.Instantiate("TasksPart", nativePart.parent, caption);
        if (part == null) return;
        part.name = "VisitAPI.TasksPart";
        var rt = (RectTransform)part.transform; rt.SetSiblingIndex(nativePart.GetSiblingIndex() + 1);
        rt.anchorMin = nativePart.anchorMin; rt.anchorMax = nativePart.anchorMax; rt.pivot = nativePart.pivot;
        rt.offsetMin = nativePart.offsetMin; rt.offsetMax = new Vector2(nativePart.offsetMax.x, nativePart.offsetMax.y + TabRowLift);
        // 1.1 TasksPart 是 VerticalLayoutGroup（Background / QuestTypeGroup / MainQuestPanel / SideQuestsPanel 从上到下）：
        // 页签栏排到 Background 之后；0.16 列表搬进来后退出布局、锚定到页签栏（50px）下方铺满
        var slot = part.transform.Find("SideQuestsPanel"); if (slot != null) slot.gameObject.SetActive(false);
        // 0.16 的 Description 原生里被列表盖住从不露面，搬家后列表底透明它就漏出来（红块）→ 永远关掉
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

    // B10：页签文案优先走服务端注入的 locale 键（跟游戏语言联动），键没到位才退回 Loc 双语兜底
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
        r.offsetMax = new Vector2(0, -50);   // 页签栏 50px 下方铺满
    }

    // 1.1 场景里页签是 spawner 下面关着的模板；激活它、挂进 ToggleGroup、按文案/字号初始化
    static Toggle Wire(Transform bar, string path, ToggleGroup group, string captionKey, int fontSize)
    {
        var t = bar != null ? bar.Find(path) : null; if (t == null) { Plugin.Log.LogWarning("[chapter] tab missing: " + path); return null; }
        t.gameObject.SetActive(true);
        var st = t.GetComponent<UISpawnableToggle>(); if (st == null || st.Toggle == null) { Plugin.Log.LogWarning("[chapter] tab not bound: " + path); return null; }
        st.Init(group); st.InitSpawnableButton(captionKey, fontSize > 0 ? fontSize : 20, null, null);
        st.Toggle.SetIsOnWithoutNotify(false);
        return st.Toggle;
    }

    // 正式版打开任务页默认落在「剧情」；0.16 的 Show 会把原生「支线」置 on，之后再把我们的「剧情」置 on（动画/面板切换都走一遍）
    [HarmonyPatch(typeof(TasksScreen), nameof(TasksScreen.Show), typeof(InventoryController), typeof(QuestController), typeof(IEftSession), typeof(NotesManager), typeof(bool))]
    public static class ShowPatch
    {
        static void Postfix(InventoryController inventoryController, QuestController questController, IEftSession session)
        {
            try
            {
                Profile = session?.Profile; Inventory = inventoryController; Quests = questController;
                ReadState.Sync();   // 兜底重试：登录那次拉已读表全败放弃后，从这里再拉（拉成过就是空操作）
                if (_story != null) { _story.isOn = false; _story.isOn = true; }
            }
            catch (System.Exception e) { Plugin.Log.LogError("[chapter] 剧情页默认落位失败: " + e.Message); }
        }
    }
}
