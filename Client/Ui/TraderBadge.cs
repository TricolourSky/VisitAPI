using System;
using System.Collections.Generic;
using System.Linq;
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
    static bool _fallback;   // call_badge.png 读不出来、退回了 visit_icon（Build 按它上金色）

    /// 金色电话角标的图；嵌入资源读不出来就退回 visit_icon 上金色。VisitArt.Load 自己按文件名缓存（缺图也缓存并报一次），外面不用再包一层
    public static Sprite Sprite
    {
        get
        {
            var s = VisitArt.Load("call_badge.png");
            if (s != null) return s;
            if (!_fallback) Plugin.Log.LogWarning("[badge] Embedded call_badge.png could not be read, falling back to gold-tinted visit_icon.png");
            _fallback = true;
            return VisitArt.Load("visit_icon.png");
        }
    }

    public static string TalkTo(Quest q)
    {
        var flagged = QuestFlags.CallTrader(q.Id) ?? QuestFlags.TalkTo(q.Id);
        if (flagged != null) return flagged;
        // 10-01 SORA：只能对话接/交的任务，点谁的访问能走到对话选项就亮谁的电话（10-18 起只算访问走得到的，见 VisitReach；隐藏商人没解锁，CanTalk 自然压掉）
        if (QuestFlags.DialogOnly(q.Id)) return VisitAPI.ChapterUI.ChapterDialogButton.TraderFor(q)?.TraderId ?? q.Template?.TraderId;
        return q.Template?.TraderId;
    }

    static bool Timed(Quest q) => q.Conds(EQuestStatus.AvailableForStart).OfType<ConditionQuest>().Any(c => c.availableAfter > 0);

    static bool NeedsTalk(Quest q, QuestController qc)
    {
        if (q?.Template == null) return false;
        // 10-01 SORA：dialogOnly 的任务「下一步在对话里」就亮金色电话——可接（剧本里有 accept 选项）、
        // 进行中 / 可交付（剧本里有 complete / handover 选项）都算；行里的「去找 X」按钮退役，指路统一走金色电话 + 金色访问。
        // 10-18 SORA：选项得是点访问走得到的；只挂在战局 / 藏身处触发点后面的（街区找 SORA 那种）不亮，访问点进去也交不了
        if (QuestFlags.DialogOnly(q.Id))
        {
            if (VisitAPI.ChapterUI.ChapterStates.Running(q.QuestStatus))
                return VisitAPI.ChapterUI.ChapterDialogButton.TraderFor(q) != null;
            return q.QuestStatus == EQuestStatus.AvailableForStart
                && VisitAPI.ChapterUI.ChapterDialogButton.TraderFor(q) != null && ChapterChain.Reachable(qc, q);
        }
        if (q.QuestStatus != EQuestStatus.AvailableForStart) return false;
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
            var profile = VisitBackend.Profile;
            if (profile?.TradersInfo != null && (traderId.Length != 24 || !profile.TradersInfo.TryGetValue(new MongoID(traderId), out var info) || info == null || !info.Unlocked)) return false;
        }
        catch (Exception e) { Plugin.Log.LogWarning("[badge] Failed to read trader unlock state, treating as talkable: " + e.Message); }
        return DialogueOpen(traderId, qc);
    }

    /// 这家商人的对话没被剧情锁着（包里没写 unlockDialogue 的商人永远开着）；TalkButton 判「能不能访问」也用它
    internal static bool DialogueOpen(string traderId, QuestController qc) => QuestFlags.DialogueUnlocked(traderId, qc) != false;

    /// 三个查表入口的开头都一样：开关开着、有活的任务控制器、名单是新的。两个开关在打补丁之前就 Bind 好了，直接读 Value
    static bool Ready(ref QuestController qc, bool on)
    {
        if (!on) return false;
        qc ??= QuestOps.Current;
        if (qc?.Quests == null) return false;
        Sweep(qc);
        return true;
    }

    /// 10-01（GitHub 商人页面卡顿 issue）：每张商人卡各自每秒把任务清单＋库存匹配全量走一遍（上交检查最重）的话，
    /// 8 张卡 = 同一份重活每秒 8 遍，商人页面周期性掉帧。所以全局 1.5 秒算一次两张名单（电话 / 可上交），各处只查表。
    /// [Badge] 两个开关也一并尊重：关掉 = 名单直接不算，角标和开销一起归零。
    static float _sweepUntil;
    static QuestController _sweepQc;
    static readonly HashSet<string> _callSet = new(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> _handSet = new(StringComparer.OrdinalIgnoreCase);

    static void Sweep(QuestController qc)
    {
        if (ReferenceEquals(qc, _sweepQc) && Time.unscaledTime < _sweepUntil) return;
        _sweepQc = qc;
        _sweepUntil = Time.unscaledTime + 1.5f;
        _callSet.Clear();
        _handSet.Clear();
        var callOn = Plugin.CallBadge.Value;
        var handOn = Plugin.HandoverBadge.Value;
        try
        {
            foreach (var q in qc.Quests)
            {
                if (q?.Template == null) continue;
                if (callOn && NeedsTalk(q, qc))
                {
                    var t = TalkTo(q);
                    if (!string.IsNullOrEmpty(t)) _callSet.Add(t);
                }
                if (handOn && q.QuestStatus == EQuestStatus.Started && HandoverReady(q, qc))
                {
                    var t = TalkTo(q);
                    if (!string.IsNullOrEmpty(t)) _handSet.Add(t);
                }
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning("[badge] badge sweep failed: " + e.Message); }
    }

    static bool HandoverReady(Quest q, QuestController qc)
    {
        foreach (var cond in q.ProgressCheckers.Keys)
        {
            if (cond is not ConditionItem || !q.CheckVisibilityStatus(cond)) continue;
            if (qc.CanHandoverItems(q.Id, cond.id, true)) return true;
        }
        return false;
    }

    public static bool Wanted(string traderId, QuestController qc = null)
    {
        if (string.IsNullOrEmpty(traderId) || !Ready(ref qc, Plugin.CallBadge.Value)) return false;
        try { return _callSet.Contains(traderId) && CanTalk(traderId, qc); }
        catch (Exception e) { Plugin.Log.LogWarning("[badge] Trader badge check failed: " + e.Message); return false; }
    }

    public static bool AnyWanted(QuestController qc = null)
    {
        if (!Ready(ref qc, Plugin.CallBadge.Value)) return false;
        try
        {
            foreach (var t in _callSet) if (CanTalk(t, qc)) return true;
            return false;
        }
        catch (Exception e) { Plugin.Log.LogWarning("[badge] Top bar badge check failed: " + e.Message); return false; }
    }

    /// 1.1.5 的三张原生角标替换图；VisitArt.Load 自己缓存，缺图每张只报一次
    static Sprite Art(string file)
    {
        var s = VisitArt.Load(file);
        if (s == null) Plugin.WarnOnce("badge/" + file, $"[badge] Embedded {file} could not be read, this 1.1.5 badge will not be replaced");
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

    public static bool HandoverWanted(string traderId, QuestController qc = null) =>
        !string.IsNullOrEmpty(traderId) && Ready(ref qc, Plugin.HandoverBadge.Value) && _handSet.Contains(traderId);

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
            catch (Exception e) { Plugin.Log.LogError("[badge] Failed to attach trader card badge (the card itself is unaffected): " + e); }
        }
    }

    [HarmonyPatch(typeof(TraderCard), nameof(TraderCard.UpdateView))]
    public static class CardUpdate
    {
        static void Postfix(TraderCard __instance)
        {
            try { __instance.GetComponent<CardBadge>()?.Refresh(); }
            catch (Exception e) { Plugin.Log.LogWarning("[badge] Trader card badge refresh failed: " + e.Message); }
        }
    }

    // 顶栏名字旁的金色电话（HeaderBadge）由 ChatInviteTaskbar 在底栏 Awake 时一并挂上

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
    TraderAvatar _avatar;

    /// 卡上的头像组件（原生角标挂在它下面）：找到一次就记住
    TraderAvatar Avatar()
    {
        if (_avatar == null) _avatar = GetComponentInChildren<TraderAvatar>(true);
        return _avatar;
    }

    LayoutGroup NativeLayout(TraderAvatar avatar)
    {
        if (_layoutChecked) return _layout;
        var start = avatar?._availableToStartQuestsIcon?.transform as RectTransform;
        if (start == null || start.parent is not RectTransform parent) return null;
        _layoutChecked = true;
        _layout = parent.GetComponent<LayoutGroup>();
        return _layout;
    }

    const float StartPad = 52f / 41f, FinishPad = 52f / 42f, HandoverPad = 49f / 41f;
    static float ColumnWidth(float slot) => slot * StartPad;

    void SizeNativeIcons(float slot)
    {
        try
        {
            var avatar = Avatar();
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
        catch (Exception e) { Plugin.Log.LogWarning("[badge] Failed to size native badge: " + e.Message); }
    }

    static readonly Vector3[] _corners = new Vector3[4];   // 每帧对齐（LateUpdate）：复用，不每帧分配

    void AlignGoldToSpacer()
    {
        if (_img == null || !_img.gameObject.activeSelf || _spacer == null || !_spacer.gameObject.activeInHierarchy) return;
        _spacer.GetWorldCorners(_corners);
        var center = (_corners[0] + _corners[2]) * 0.5f;
        if ((_img.transform.position - center).sqrMagnitude > 0.0001f) _img.transform.position = center;
    }

    void ShiftNativeIcons(bool on, float shift)
    {
        try
        {
            var avatar = Avatar();
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
        catch (Exception e) { Plugin.Log.LogWarning("[badge] Failed to shift native badge aside: " + e.Message); }
    }

    public void Refresh()
    {
        if (_img == null) return;
        var on = TraderBadge.Wanted(_traderId, _qc);
        if (_img.gameObject.activeSelf != on) TalkButton.Retint();
        var w = ((RectTransform)transform).rect.width;
        if (w <= 1f) w = 150f;
        var slot = w * visible;
        SizeNativeIcons(slot);
        if (on)
        {
            var rt = (RectTransform)_img.transform;
            var size = Mathf.Clamp(slot * TraderBadge.PaddingScale, 16f, 64f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = Corner(w);
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
            var avatar = Avatar();
            SwapNativeIcons(avatar);
            var on = TraderBadge.HandoverWanted(_traderId, _qc);
            if (!on) { if (_hand != null && _hand.gameObject.activeSelf) _hand.gameObject.SetActive(false); return; }
            if (_hand == null)
            {
                if (TraderBadge.HandoverSprite == null) return;
                _hand = TraderBadge.Build((RectTransform)transform, "VisitHandoverBadge", TraderBadge.HandoverSprite);
            }
            var rt = (RectTransform)_hand.transform;
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
                rt.anchoredPosition = Corner(w) - new Vector2(0f, gold ? slot : 0f);
                rt.SetAsLastSibling();
            }
            if (!_hand.gameObject.activeSelf) _hand.gameObject.SetActive(true);
        }
        catch (Exception e) { Plugin.Log.LogWarning("[badge] Hand-over badge refresh failed: " + e.Message); }
    }
    const float visible = 0.15f;
    static Vector2 Corner(float w) => new Vector2(-w * (0.106f + visible / 2f), -w * (0.11f + visible / 2f));

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
        var nick = VisitBackend.Profile?.Nickname;
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
