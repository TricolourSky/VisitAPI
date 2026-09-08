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
        if (show) Place(__instance);
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
