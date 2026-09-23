using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Quests;
using EFT.UI;
using EFT.UI.Screens;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VisitAPI.Native;

public static class TraderBadge
{
    public static readonly Color Gold = new Color32(0xD8, 0xBC, 0x40, 0xFF);
    internal const float PaddingScale = 36f / 27f;
    static Sprite _sprite;
    static bool _spriteTried, _fallback;

    public static Sprite Sprite
    {
        get
        {
            if (_spriteTried) return _sprite;
            _spriteTried = true;
            _sprite = VisitArt.Load("call_badge.png");
            if (_sprite == null) { _fallback = true; _sprite = VisitArt.Load("visit_icon.png"); Plugin.Log.LogWarning("[badge] 内嵌 call_badge.png 读不到，退回染金的 visit_icon.png"); }
            return _sprite;
        }
    }

    public static string TalkTo(Quest q) => QuestFlags.CallTrader(q.Id) ?? QuestFlags.TalkTo(q.Id) ?? q.Template?.TraderId;

    static bool Timed(Quest q) =>
        q.Template?.Conditions != null && q.Template.Conditions.TryGetValue(EQuestStatus.AvailableForStart, out var cc)
        && cc.OfType<ConditionQuest>().Any(c => c.availableAfter > 0);

    static bool NeedsTalk(Quest q, QuestController qc)
    {
        if (q?.Template == null || q.QuestStatus != EQuestStatus.AvailableForStart) return false;
        if (QuestFlags.CallTrader(q.Id) != null) return ChapterChain.Reachable(qc, q);
        if (QuestFlags.Get(q.Id)?.Story == true) return false;
        return QuestFlags.IsStory(q.Id) && Timed(q) && !QuestFlags.IsChapter(q.Id) && !QuestFlags.AutoStart(q.Id) && QuestFlags.StartAfter(q.Id) == null
            && ChapterChain.Reachable(qc, q);
    }

    static bool CanTalk(string traderId, QuestController qc)
    {
        if (string.IsNullOrEmpty(traderId)) return false;
        try
        {
            var profile = Singleton<ClientApplication<IEftSession>>.Instance?.GetClientBackEndSession()?.Profile;
            if (profile?.TradersInfo != null)
            {
                if (traderId.Length != 24 || !profile.TradersInfo.TryGetValue(new MongoID(traderId), out var info) || info == null || !info.Unlocked) return false;
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning("[badge] 读商人解锁状态失败，按可谈处理: " + e.Message); }
        return QuestFlags.DialogueUnlocked(traderId, qc) != false;
    }

    static QuestController Controller => ChapterChain.Controller ?? ChapterTab.Quests ?? QuestOps.Resolve();

    public static List<string> Lit(string traderId, QuestController qc = null)
    {
        qc ??= Controller;
        if (qc?.Quests == null || string.IsNullOrEmpty(traderId)) return new List<string>();
        try { return qc.Quests.Where(q => NeedsTalk(q, qc) && string.Equals(TalkTo(q), traderId, StringComparison.OrdinalIgnoreCase)).Select(q => q.Id).ToList(); }
        catch { return new List<string>(); }
    }

    public static bool Wanted(string traderId, QuestController qc = null)
    {
        if (!Plugin.CallBadge.Value || string.IsNullOrEmpty(traderId)) return false;
        qc ??= Controller;
        if (qc?.Quests == null) return false;
        try { return CanTalk(traderId, qc) && qc.Quests.Any(q => NeedsTalk(q, qc) && string.Equals(TalkTo(q), traderId, StringComparison.OrdinalIgnoreCase)); }
        catch (Exception e) { Plugin.Log.LogWarning("[badge] 商人角标判定失败: " + e.Message); return false; }
    }

    public static bool AnyWanted(QuestController qc = null)
    {
        if (!Plugin.CallBadge.Value) return false;
        qc ??= Controller;
        if (qc?.Quests == null) return false;
        try { return qc.Quests.Any(q => NeedsTalk(q, qc) && CanTalk(TalkTo(q), qc)); }
        catch (Exception e) { Plugin.Log.LogWarning("[badge] 顶栏角标判定失败: " + e.Message); return false; }
    }

    static readonly Dictionary<string, Sprite> _sprites = new();
    static Sprite Art(string file)
    {
        if (_sprites.TryGetValue(file, out var s)) return s;
        s = VisitArt.Load(file);
        if (s == null) Plugin.Log.LogWarning($"[badge] 内嵌 {file} 读不到，这枚 1.1.5 角标不换");
        _sprites[file] = s;
        return s;
    }
    public static Sprite HandoverSprite => Art("handover_badge.png");
    public static Sprite StartSprite => Art("start_badge.png");
    public static Sprite FinishSprite => Art("finish_badge.png");

    internal static void Swap(GameObject icon, Sprite sprite)
    {
        if (icon == null || sprite == null) return;
        var img = icon.GetComponent<Image>() ?? icon.GetComponentInChildren<Image>(true);
        if (img == null || img.sprite == sprite) return;
        img.sprite = sprite;
        img.color = Color.white;
        img.preserveAspect = true;
    }

    static readonly Dictionary<string, (float until, bool on)> _handCache = new(StringComparer.OrdinalIgnoreCase);

    public static bool HandoverWanted(string traderId, QuestController qc = null)
    {
        if (!Plugin.HandoverBadge.Value || string.IsNullOrEmpty(traderId)) return false;
        if (_handCache.TryGetValue(traderId, out var cached) && Time.unscaledTime < cached.until) return cached.on;
        qc ??= Controller;
        var on = false;
        if (qc?.Quests != null)
            try
            {
                foreach (var q in qc.Quests)
                {
                    if (q?.Template == null || q.QuestStatus != EQuestStatus.Started) continue;
                    if (!string.Equals(TalkTo(q), traderId, StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (var cond in q.ProgressCheckers.Keys)
                    {
                        if (cond is not ConditionItem || !q.CheckVisibilityStatus(cond)) continue;
                        if (qc.CanHandoverItems(q.Id, cond.id, true)) { on = true; break; }
                    }
                    if (on) break;
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("[badge] 上交角标判定失败: " + e.Message); }
        _handCache[traderId] = (Time.unscaledTime + 1f, on);
        return on;
    }

    [HarmonyPatch(typeof(TraderCard), nameof(TraderCard.Show))]
    public static class CardShow
    {
        static void Postfix(TraderCard __instance, Profile.TraderInfo trader, QuestController questController)
        {
            try
            {
                var view = __instance.GetComponent<CardBadge>() ?? __instance.gameObject.AddComponent<CardBadge>();
                view.Bind(trader?.Id, questController);
            }
            catch (Exception e) { Plugin.Log.LogError("[badge] 商人卡片角标挂载失败（卡片本体不受影响）: " + e); }
        }
    }

    [HarmonyPatch(typeof(TraderCard), nameof(TraderCard.UpdateView))]
    public static class CardUpdate
    {
        static void Postfix(TraderCard __instance)
        {
            try { __instance.GetComponent<CardBadge>()?.Refresh(); }
            catch (Exception e) { Plugin.Log.LogWarning("[badge] 商人卡片角标刷新失败: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(MenuTaskBar), "Awake")]
    public static class TaskBarAwake
    {
        static void Postfix(MenuTaskBar __instance)
        {
            try { if (__instance.GetComponent<HeaderBadge>() == null) __instance.gameObject.AddComponent<HeaderBadge>(); }
            catch (Exception e) { Plugin.Log.LogError("[badge] 顶栏角标挂载失败（任务栏本体不受影响）: " + e); }
        }
    }

    [HarmonyPatch(typeof(MenuTaskBar), nameof(MenuTaskBar.OnScreenChanged))]
    public static class ScreenChanged
    {
        static void Postfix(EEftScreenType eftScreenType) => HeaderBadge.Screen = eftScreenType;
    }

    internal static Image Build(RectTransform parent, string name, Sprite sprite = null)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = sprite ?? Sprite;
        img.color = sprite == null && _fallback ? Gold : Color.white;
        img.raycastTarget = false;
        img.preserveAspect = true;
        go.SetActive(false);
        return img;
    }
}

public class CardBadge : MonoBehaviour
{
    string _traderId;
    QuestController _qc;
    Image _img;
    int _seen = -1;
    float _next;

    public void Bind(string traderId, QuestController qc)
    {
        _traderId = traderId;
        _qc = qc;
        if (_img == null && TraderBadge.Sprite != null)
        {
            _img = TraderBadge.Build((RectTransform)transform, "VisitCallBadge");
            var rt = (RectTransform)_img.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
        }
        Refresh();
    }

    readonly Dictionary<RectTransform, Vector2> _nativeHome = new();
    bool _shifted;
    RectTransform _spacer;
    LayoutGroup _layout;
    bool _layoutChecked;
    static bool _layoutLogged;

    LayoutGroup NativeLayout(TraderAvatar avatar)
    {
        if (_layoutChecked) return _layout;
        var start = avatar?._availableToStartQuestsIcon?.transform as RectTransform;
        if (start == null || start.parent is not RectTransform parent) return null;
        _layoutChecked = true;
        _layout = parent.GetComponent<LayoutGroup>();
        if (!_layoutLogged)
        {
            _layoutLogged = true;
            Plugin.Log.LogInfo($"[badge] 原生角标父物体 '{parent.name}'：布局组 {(_layout != null ? _layout.GetType().Name : "无")}，可接角标锚点 {start.anchorMin}~{start.anchorMax} 轴心 {start.pivot} 坐标 {start.anchoredPosition} 尺寸 {start.sizeDelta}");
        }
        return _layout;
    }

    const float StartPad = 52f / 41f, FinishPad = 52f / 42f, HandoverPad = 49f / 41f;
    static float ColumnWidth(float slot) => slot * StartPad;

    void SizeNativeIcons(float slot)
    {
        try
        {
            var avatar = GetComponentInChildren<TraderAvatar>(true);
            if (avatar == null || slot <= 1f) return;
            var layout = NativeLayout(avatar);
            var width = ColumnWidth(slot);
            foreach (var (go, pad) in new[] { (avatar._availableToStartQuestsIcon, StartPad), (avatar._availableToFinishQuestsIcon, FinishPad) })
            {
                if (go == null || go.transform is not RectTransform rt) continue;
                var size = new Vector2(width, slot * pad);
                if (layout != null)
                {
                    var le = rt.GetComponent<LayoutElement>() ?? rt.gameObject.AddComponent<LayoutElement>();
                    if (Mathf.Abs(le.preferredWidth - size.x) > 0.5f || Mathf.Abs(le.preferredHeight - size.y) > 0.5f) { le.preferredWidth = size.x; le.preferredHeight = size.y; }
                }
                if ((rt.sizeDelta - size).sqrMagnitude > 0.25f) rt.sizeDelta = size;
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning("[badge] 原生角标定尺寸失败: " + e.Message); }
    }

    void AlignGoldToSpacer()
    {
        if (_img == null || !_img.gameObject.activeSelf || _spacer == null || !_spacer.gameObject.activeInHierarchy) return;
        var corners = new Vector3[4];
        _spacer.GetWorldCorners(corners);
        var center = (corners[0] + corners[2]) * 0.5f;
        if ((_img.transform.position - center).sqrMagnitude > 0.0001f) _img.transform.position = center;
    }

    void ShiftNativeIcons(bool on, float shift)
    {
        try
        {
            var avatar = GetComponentInChildren<TraderAvatar>(true);
            if (avatar == null) return;
            var layout = NativeLayout(avatar);
            if (layout != null)
            {
                if (_spacer == null)
                {
                    if (!on) return;
                    var go = new GameObject("VisitCallBadgeSpacer", typeof(RectTransform), typeof(LayoutElement));
                    _spacer = (RectTransform)go.transform;
                    _spacer.SetParent(layout.transform, false);
                }
                var le = _spacer.GetComponent<LayoutElement>();
                var cell = ColumnWidth(shift);
                le.preferredWidth = le.preferredHeight = cell;
                _spacer.sizeDelta = new Vector2(cell, cell);
                if (_spacer.GetSiblingIndex() != 0) _spacer.SetAsFirstSibling();
                if (_spacer.gameObject.activeSelf != on) _spacer.gameObject.SetActive(on);
                if (on && layout.transform is RectTransform lrt) LayoutRebuilder.ForceRebuildLayoutImmediate(lrt);
                return;
            }
            if (!on && !_shifted) return;
            foreach (var go in new[] { avatar._availableToStartQuestsIcon, avatar._availableToFinishQuestsIcon })
            {
                if (go == null || go.transform is not RectTransform rt) continue;
                if (!_nativeHome.TryGetValue(rt, out var home))
                {
                    if (!on) continue;
                    home = rt.anchoredPosition; _nativeHome[rt] = home;
                }
                var want = on ? home + new Vector2(0f, -shift) : home;
                if ((rt.anchoredPosition - want).sqrMagnitude > 0.01f) rt.anchoredPosition = want;
            }
            _shifted = on;
        }
        catch (Exception e) { Plugin.Log.LogWarning("[badge] 原生角标让位失败: " + e.Message); }
    }

    public void Refresh()
    {
        if (_img == null) return;
        var on = TraderBadge.Wanted(_traderId, _qc);
        if (_img.gameObject.activeSelf != on)
        {
            TalkButton.Retint();
            Plugin.Log.LogInfo(on ? $"[badge] 商人 {_traderId} 金色电话亮起，任务: {string.Join(", ", TraderBadge.Lit(_traderId, _qc))}" : $"[badge] 商人 {_traderId} 金色电话熄灭");
        }
        var w = ((RectTransform)transform).rect.width;
        if (w <= 1f) w = 150f;
        var slot = w * visible;
        if (Plugin.CallBadge.Value || Plugin.HandoverBadge.Value) SizeNativeIcons(slot);
        if (on)
        {
            var rt = (RectTransform)_img.transform;
            var size = Mathf.Clamp(w * visible * TraderBadge.PaddingScale, 16f, 64f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(-w * (0.106f + visible / 2f), -w * (0.11f + visible / 2f));
            rt.SetAsLastSibling();
            ShiftNativeIcons(true, slot);
        }
        else ShiftNativeIcons(false, 0f);
        if (_img.gameObject.activeSelf != on) _img.gameObject.SetActive(on);
        RefreshHandover(on, slot, w);
        AlignGoldToSpacer();
    }

    Image _hand;
    bool _swapped;
    void SwapNativeIcons(TraderAvatar avatar)
    {
        if (_swapped || avatar == null || !Plugin.HandoverBadge.Value) return;
        _swapped = true;
        TraderBadge.Swap(avatar._availableToStartQuestsIcon, TraderBadge.StartSprite);
        TraderBadge.Swap(avatar._availableToFinishQuestsIcon, TraderBadge.FinishSprite);
    }

    void RefreshHandover(bool gold, float slot, float w)
    {
        try
        {
            if (!_swapped) SwapNativeIcons(GetComponentInChildren<TraderAvatar>(true));
            var on = TraderBadge.HandoverWanted(_traderId, _qc);
            if (!on) { if (_hand != null && _hand.gameObject.activeSelf) _hand.gameObject.SetActive(false); return; }
            if (_hand == null)
            {
                if (TraderBadge.HandoverSprite == null) return;
                _hand = TraderBadge.Build((RectTransform)transform, "VisitHandoverBadge", TraderBadge.HandoverSprite);
            }
            var rt = (RectTransform)_hand.transform;
            var avatar = GetComponentInChildren<TraderAvatar>(true);
            SwapNativeIcons(avatar);
            var start = avatar != null && avatar._availableToStartQuestsIcon != null ? avatar._availableToStartQuestsIcon.transform as RectTransform : null;
            if (start != null && start.parent is RectTransform nativeParent)
            {
                if (rt.parent != nativeParent) rt.SetParent(nativeParent, false);
                var size = new Vector2(ColumnWidth(slot), slot * HandoverPad);
                rt.sizeDelta = size;
                if (NativeLayout(avatar) != null)
                {
                    var le = rt.GetComponent<LayoutElement>() ?? rt.gameObject.AddComponent<LayoutElement>();
                    le.preferredWidth = size.x; le.preferredHeight = size.y;
                    if (rt.GetSiblingIndex() != nativeParent.childCount - 1) rt.SetAsLastSibling();
                }
                else
                {
                    rt.anchorMin = start.anchorMin; rt.anchorMax = start.anchorMax; rt.pivot = start.pivot;
                    var home = _nativeHome.TryGetValue(start, out var h) ? h : start.anchoredPosition;
                    float? lowest = gold ? home.y : null;
                    foreach (var go in new[] { avatar._availableToStartQuestsIcon, avatar._availableToFinishQuestsIcon })
                        if (go != null && go.activeInHierarchy && go.transform is RectTransform nrt)
                            lowest = lowest.HasValue ? Mathf.Min(lowest.Value, nrt.anchoredPosition.y) : nrt.anchoredPosition.y;
                    rt.anchoredPosition = new Vector2(home.x, lowest.HasValue ? lowest.Value - slot : home.y);
                    rt.SetAsLastSibling();
                }
            }
            else
            {
                if (rt.parent != transform) rt.SetParent(transform, false);
                rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(slot, slot);
                rt.anchoredPosition = new Vector2(-w * (0.106f + visible / 2f), -w * (0.11f + visible / 2f) - (gold ? slot : 0f));
                rt.SetAsLastSibling();
            }
            if (!_hand.gameObject.activeSelf) _hand.gameObject.SetActive(true);
        }
        catch (Exception e) { Plugin.Log.LogWarning("[badge] 上交角标刷新失败: " + e.Message); }
    }
    const float visible = 0.15f;

    void LateUpdate()
    {
        if (_img == null) return;
        if (ChapterEvents.Changed(ref _seen) || Time.unscaledTime >= _next) { _next = Time.unscaledTime + 1f; Refresh(); }
        AlignGoldToSpacer();
    }
}

public class HeaderBadge : MonoBehaviour
{
    public static EEftScreenType Screen = EEftScreenType.MainMenu;
    TMP_Text _label;
    Image _img;
    EEftScreenType _seenScreen;
    float _next, _retry;

    static string Nickname()
    {
        try { return Singleton<ClientApplication<IEftSession>>.Instance?.GetClientBackEndSession()?.Profile?.Nickname; }
        catch { return null; }
    }

    void LateUpdate()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.5f;
        if (Screen != EEftScreenType.MainMenu) { Hide(); _seenScreen = Screen; return; }
        if (_seenScreen != Screen) { _seenScreen = Screen; _label = null; _retry = 0f; }
        if (_label == null || !_label.isActiveAndEnabled)
        {
            if (Time.unscaledTime < _retry) { Hide(); return; }
            if (!Find()) { _retry = Time.unscaledTime + 5f; Hide(); return; }
        }
        var on = TraderBadge.AnyWanted();
        if (on) Place();
        if (_img != null && _img.gameObject.activeSelf != on) _img.gameObject.SetActive(on);
    }

    void Hide() { if (_img != null && _img.gameObject.activeSelf) _img.gameObject.SetActive(false); }

    bool Find()
    {
        var nick = Nickname();
        if (string.IsNullOrEmpty(nick)) return false;
        TMP_Text best = null;
        foreach (var t in FindObjectsOfType<TextMeshProUGUI>())
        {
            if (t == null || !t.isActiveAndEnabled || t.text == null || t.text.Trim() != nick) continue;
            if (t.GetComponentInParent<CardBadge>() != null) continue;
            if (best == null || t.fontSize > best.fontSize) best = t;
        }
        if (best == null) return false;
        if (best != _label || _img == null)
        {
            if (_img != null) Destroy(_img.gameObject);
            _label = best;
            if (TraderBadge.Sprite == null) return false;
            _img = TraderBadge.Build((RectTransform)_label.transform, "VisitCallBadgeHeader");
            Plugin.Log.LogInfo($"[badge] 顶栏角标挂到昵称文字 '{_label.name}'（字号 {_label.fontSize:0.#}）");
        }
        return true;
    }

    void Place()
    {
        if (_img == null || _label == null) return;
        var rt = (RectTransform)_img.transform;
        var size = Mathf.Clamp(_label.fontSize * 1.1f * TraderBadge.PaddingScale, 24f, 64f);
        rt.sizeDelta = new Vector2(size, size);
        rt.pivot = new Vector2(0f, 0.5f);
        if (_label.horizontalAlignment == HorizontalAlignmentOptions.Left)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(_label.preferredWidth + 8f, 0f);
        }
        else
        {
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(8f, 0f);
        }
        rt.SetAsLastSibling();
    }
}
