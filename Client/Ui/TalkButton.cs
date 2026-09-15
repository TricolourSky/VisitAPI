using System.Collections;
using System.Linq;
using Comfort.Common;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using VisitAPI.Dialog;

namespace VisitAPI.Native;

/// <summary>
/// 商人屏上的「访问」按钮 + 两条路的分岔决策：
/// 有 `&lt;traderId&gt;.dlg` → 自定义对话（DialogOpener）；无 `.dlg` 有房间包 → 原生 narrate（NarrateEntry）。
/// </summary>
[HarmonyPatch(typeof(TraderScreensGroup), "SelectTrader")]
public static class TalkButton
{
    static GameObject _button;
    static TraderScreensGroup _screen;
    static bool _opening;

    // 阶段四单点隔离：SelectTrader 是商人屏主链路，这里炸了只损失访问按钮
    static void Postfix(TraderScreensGroup __instance)
    {
        try { Refresh(__instance); }
        catch (System.Exception e) { Plugin.Log.LogError("[talk] 访问按钮刷新失败（商人屏不受影响）: " + e); }
    }

    static void Refresh(TraderScreensGroup __instance)
    {
        _screen = __instance;
        var traderId = __instance.Trader?.Id;
        var tree = DialogFiles.Tree(traderId);
        var canNarrate = tree == null && traderId != null && NarrateEntry.CanVisit(traderId) && DialogueOpen(traderId, __instance.QuestController);
        var show = !TabRouter.DialogWindowOpen && (canNarrate || (tree != null && TabPasses(tree, __instance)));
        if (_button == null && show) _button = TalkButtonUi.Build(__instance, Open);
        if (_button == null) return;
        _button.SetActive(show);
        if (show) { Place(__instance); TalkButtonUi.Tint(_button, TraderBadge.Wanted(traderId, __instance.QuestController)); }
    }

    /// 商人屏开着时角标状态变了（定时到点 / 接完任务）→ 页签金色跟着变（CardBadge 状态翻转时调，09-13 审查）
    public static void Retint()
    {
        try
        {
            if (_button == null || !_button.activeSelf || _screen == null) return;
            var traderId = _screen.Trader?.Id;
            if (traderId != null) TalkButtonUi.Tint(_button, TraderBadge.Wanted(traderId, _screen.QuestController));
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("[talk] 访问按钮重上色失败: " + e.Message); }
    }

    /// 原生访问按剧情逐步开放（09-08）：任务 JSON `visitapi.unlockDialogue` 点名了这位商人的话，要有一条已完成才出按钮；没人点名照旧
    static bool DialogueOpen(string traderId, QuestController qc)
    {
        var state = QuestFlags.DialogueUnlocked(traderId, qc);
        if (state == false) Plugin.Log.LogDebug($"[talk] {traderId} 的对话还没解锁（剧情未到），不出访问按钮");
        return state != false;
    }

    static bool TabPasses(DialogTree tree, TraderScreensGroup tsg)
    {
        if (tree.TabQuestId == null) return true;
        Quest quest = tsg.QuestController?.Quests?.GetConditional(tree.TabQuestId);
        return quest != null && tree.TabStatuses.Contains((int)quest.QuestStatus);
    }

    static void Place(TraderScreensGroup tsg)
    {
        var rt = (RectTransform)_button.transform;
        var parent = (RectTransform)rt.parent;
        var corners = new Vector3[4];
        ((RectTransform)tsg._traderCardsContainer).GetWorldCorners(corners);
        var topGap = parent.InverseTransformPoint(corners[1]).y - parent.rect.yMax;
        rt.anchoredPosition = new Vector2(Plugin.TalkOffsetX.Value, topGap + Plugin.TalkOffsetY.Value);
    }

    static void Open()
    {
        if (_opening || TabRouter.DialogWindowOpen || DialogScreenTracker.Open) return;
        _opening = true;
        Singleton<GUISounds>.Instance.PlayUISound(EUISoundType.ButtonClick);
        var id = _screen.Trader.Id;
        var tree = DialogFiles.Tree(id);
        if (tree == null && NarrateEntry.CanVisit(id) && DialogueOpen(id, _screen.QuestController))
        {
            NarrateEntry.Visit(id);
            Plugin.Instance.StartCoroutine(Rearm());
        }
        else if (tree == null)
        {
            _opening = false;
            Plugin.Log.LogWarning("[talk] no .dlg for " + id);
        }
        else if (!DialogOpener.TryOpen(tree, _screen.Profile, _screen.QuestController, _screen.InventoryController, _screen, out var error))
        {
            _opening = false;
            Plugin.Log.LogWarning("[talk] open failed: " + error);
        }
        else Plugin.Instance.StartCoroutine(Rearm());
    }

    static IEnumerator Rearm()
    {
        yield return UiWait.Until(() => DialogScreenTracker.Open, 300);
        _opening = false;   // 等到或超时都复位，按钮不会永久锁死
    }
}

public static class TalkButtonUi
{
    public static GameObject Build(TraderScreensGroup screen, UnityAction onClick)
    {
        Sprite normal = VisitArt.Load("visit_tab.png"), hover = VisitArt.Load("visit_tab_hover.png"), icon = VisitArt.Load("visit_icon.png");
        if (normal == null || hover == null) return null;
        var go = new GameObject("VisitTalkButton", typeof(RectTransform), typeof(Image), typeof(Button));
        var rt = (RectTransform)go.transform;
        rt.SetParent(screen._closeButton.transform.parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(150f, 26f);
        go.GetComponent<Image>().sprite = normal;
        var btn = go.GetComponent<Button>();
        btn.transition = Selectable.Transition.SpriteSwap;
        btn.spriteState = new SpriteState { highlightedSprite = hover, pressedSprite = hover, disabledSprite = normal };
        btn.onClick.AddListener(onClick);
        if (icon != null) { var im = Child(rt, "Icon", new Vector2(-24f, 0f), new Vector2(17f, 16f)).AddComponent<Image>(); im.sprite = icon; im.raycastTarget = false; }
        var label = Child(rt, "Label", new Vector2(7f, -1f), new Vector2(104f, 26f)).AddComponent<TextMeshProUGUI>();
        label.font = screen.GetComponentsInChildren<TMP_Text>(true).Select(t => t.font).FirstOrDefault(f => f != null);
        label.text = Loc.Pick("访问", "VISIT");
        label.fontSize = 16f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;
        return go;
    }

    // 09-12 第 3 轮 SORA 正式版对照图：1.1 是**整个页签变金**（上 #CABE5A → 下 #7B7A44 的竖向渐变），图标和字变深色（取样 #354F46），
    // 不是图标和字变金。金页签按 1.1 的渐变现画：拿 visit_tab.png 的形状（alpha）当遮罩，逐行填 1.1 量出来的两端色——形状是 1.1 的、颜色是 1.1 的，不另做图。
    static readonly Color32 GoldTop = new(0xCA, 0xBE, 0x5A, 0xFF), GoldBottom = new(0x7B, 0x7A, 0x44, 0xFF), DarkInk = new(0x35, 0x4F, 0x46, 0xFF);
    static Sprite _goldTab;

    static Sprite GoldTab()
    {
        if (_goldTab != null) return _goldTab;
        var shape = VisitArt.Load("visit_tab.png");
        if (shape == null || shape.texture == null) return null;
        try
        {
            var src = shape.texture;
            var w = src.width; var h = src.height;
            var pixels = src.GetPixels32();
            for (var y = 0; y < h; y++)
            {
                var c = Color32.Lerp(GoldBottom, GoldTop, h <= 1 ? 1f : (float)y / (h - 1));   // 贴图行 0 在底部
                // 原件最上面一行是 alpha=48 的淡高光线；按钮缩放到 150×26 画时这 1 像素被重采样成一串虚点（09-12 第 5 轮 SORA「访问按钮上面有个虚线」），
                // 金底上看得见、暗底上看不见——金页签上把这种半透明边去掉
                for (var x = 0; x < w; x++) { var i = y * w + x; c.a = pixels[i].a < 64 ? (byte)0 : pixels[i].a; pixels[i] = c; }
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            tex.Apply();
            _goldTab = Sprite.Create(tex, shape.rect, new Vector2(0.5f, 0.5f), shape.pixelsPerUnit, 0u, SpriteMeshType.FullRect, shape.border);
        }
        catch (System.Exception e) { Plugin.Log.LogWarning("[talk] 金色访问页签生成失败，退回图标变金: " + e.Message); }
        return _goldTab;
    }

    /// 选中的商人有「联系过你、等你来谈」的任务时：页签整块变 1.1 的金色渐变、图标和字变深色；否则还原（暗底 + 白字，悬停换亮底）
    public static void Tint(GameObject button, bool gold)
    {
        var tab = button.GetComponent<Image>();
        var btn = button.GetComponent<Button>();
        var normal = VisitArt.Load("visit_tab.png");
        var hover = VisitArt.Load("visit_tab_hover.png");
        var goldTab = gold ? GoldTab() : null;
        if (tab != null && btn != null)
        {
            if (goldTab != null)
            {
                // 09-13 SORA：金页签鼠标移上去要和原版一样白色高亮——常态金底，悬停/按下换成原来的亮底
                tab.sprite = goldTab;
                btn.spriteState = new SpriteState { highlightedSprite = hover ?? goldTab, pressedSprite = hover ?? goldTab, disabledSprite = goldTab };
            }
            else if (normal != null)
            {
                tab.sprite = normal;
                btn.spriteState = new SpriteState { highlightedSprite = hover, pressedSprite = hover, disabledSprite = normal };
            }
        }
        // 09-12 第 4 轮 SORA：正式版是**金底白字**（第 3 轮取样到的深色是图标描边的混色，不是字色）——金页签上图标和字保持白
        var ink = gold && goldTab == null ? TraderBadge.Gold : Color.white;
        var icon = button.transform.Find("Icon")?.GetComponent<Image>();
        if (icon != null) icon.color = ink;
        var label = button.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
        if (label != null) label.color = ink;
    }

    static GameObject Child(RectTransform parent, string name, Vector2 pos, Vector2 size)
    {
        var rt = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt.gameObject;
    }
}
