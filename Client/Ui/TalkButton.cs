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

[HarmonyPatch(typeof(TraderScreensGroup), "SelectTrader")]
public static class TalkButton
{
    static GameObject _button;
    static TraderScreensGroup _screen;
    static bool _opening;

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
        var show = !TabRouter.Active && (canNarrate || (tree != null && TabPasses(tree, __instance)));
        if (_button == null && show) _button = TalkButtonUi.Build(__instance, Open);
        if (_button == null) return;
        _button.SetActive(show);
        if (show) { Place(__instance); TalkButtonUi.Tint(_button, TraderBadge.Wanted(traderId, __instance.QuestController)); }
    }

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
        if (_opening || TabRouter.Active || DialogScreenTracker.Open) return;
        _opening = true;
        try
        {
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
        catch (System.Exception e)
        {
            _opening = false;
            Plugin.Log.LogError("[talk] 打开访问失败（按钮已解锁，可以再点）: " + e);
        }
    }

    static IEnumerator Rearm()
    {
        yield return UiWait.Until(() => DialogScreenTracker.Open, 300);
        _opening = false;
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

    static readonly Color32 GoldTop = new(0xCA, 0xBE, 0x5A, 0xFF), GoldBottom = new(0x7B, 0x7A, 0x44, 0xFF);
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
                var c = Color32.Lerp(GoldBottom, GoldTop, h <= 1 ? 1f : (float)y / (h - 1));
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
                tab.sprite = goldTab;
                btn.spriteState = new SpriteState { highlightedSprite = hover ?? goldTab, pressedSprite = hover ?? goldTab, disabledSprite = goldTab };
            }
            else if (normal != null)
            {
                tab.sprite = normal;
                btn.spriteState = new SpriteState { highlightedSprite = hover, pressedSprite = hover, disabledSprite = normal };
            }
        }
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
