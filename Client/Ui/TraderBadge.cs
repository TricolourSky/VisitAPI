using System;
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

/// <summary>
/// 1.1 的金色电话角标（SORA 09-11 截图：Jaeger 卡片右上角一枚、主菜单顶栏昵称后一枚）。0.16 没有这套 UI——
/// 对话动作的 needNotification 反序列化进 DialogNotifiedAction.Notify 之后无人读，TraderCard 也没有角标位（Dev_Note #132）。
///
/// 判据（SORA 09-12 定）：**定时到了、等玩家去对话接的任务**——剧情任务处于「可接」、不自动接、且它的可接前置带 availableAfter
/// （「几个小时后商人联系你」就是这种任务）。挂在哪位商人头上 = 任务对话（dialogueId）的主商人（服务端查对话表随 flags 下发），没有就是任务自己的商人。
/// 第一版（进行中的对话目标 / 可交任务也亮）被 SORA 否掉：满屏金图标。
///
/// 图形是 1.1 原件：`call_badge.png` = 1.1 客户端 resources.assets 里 SpriteAtlas「ChatBar」的 `Social_Trader_Chat_Full-Call-Icon (2)_3`
/// （36×34，金色电话 + 带字气泡、自带深色描边；同一图集里 `_0` 就是访问页签那枚灰白电话 visit_icon.png，`_1` 是不带描边的亮金版），
/// 09-12 用 .work\tools\SpriteRip 从 1.1 的 resources.assets 切出来的，原色使用、不上色、不描边。
/// （之前试过 0.16 的 Dialog_Icons_Talk（聊天气泡）和把 visit_icon.png 染金，SORA 两轮实机都判「不像、没质感」。）
/// </summary>
public static class TraderBadge
{
    /// SORA 09-11 截图取样：顶栏 #D8BC40、卡片 #D2C651（小图有抗锯齿混色），取顶栏那枚——现在只给「找不到原件」时的退路上色用
    public static readonly Color Gold = new Color32(0xD8, 0xBC, 0x40, 0xFF);
    /// 原件 36×34 里实心只有 27×26（四周约 4 像素透明），按「实心尺寸」定大小时要乘这个系数
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

    /// 这条任务「去找谁说话」：服务端按 dialogueId 查出来的商人，没有就是任务自己的商人
    public static string TalkTo(Quest q) => QuestFlags.TalkTo(q.Id) ?? q.Template?.TraderId;

    /// 这条任务的可接前置里有没有 availableAfter 定时（1.1「过一段时间商人联系你」的任务）
    static bool Timed(Quest q) =>
        q.Template?.Conditions != null && q.Template.Conditions.TryGetValue(EQuestStatus.AvailableForStart, out var cc)
        && cc.OfType<ConditionQuest>().Any(c => c.availableAfter > 0);

    /// 商人现在有没有「联系过你、等你来谈」的任务
    static bool NeedsTalk(Quest q) =>
        q?.Template != null && QuestFlags.IsStory(q.Id) && q.QuestStatus == EQuestStatus.AvailableForStart && !QuestFlags.AutoStart(q.Id) && Timed(q);

    /// 菜单里能拿到的任务控制器：商人屏/剧情页记下的那个优先
    static QuestController Controller => ChapterChain.Controller ?? ChapterTab.Quests ?? QuestOps.Resolve();

    public static bool Wanted(string traderId, QuestController qc = null)
    {
        if (!Plugin.CallBadge.Value || string.IsNullOrEmpty(traderId)) return false;
        qc ??= Controller;
        if (qc?.Quests == null) return false;
        try { return qc.Quests.Any(q => NeedsTalk(q) && string.Equals(TalkTo(q), traderId, StringComparison.OrdinalIgnoreCase)); }
        catch (Exception e) { Plugin.Log.LogWarning("[badge] 商人角标判定失败: " + e.Message); return false; }
    }

    public static bool AnyWanted(QuestController qc = null)
    {
        if (!Plugin.CallBadge.Value) return false;
        qc ??= Controller;
        if (qc?.Quests == null) return false;
        try { return qc.Quests.Any(NeedsTalk); }
        catch (Exception e) { Plugin.Log.LogWarning("[badge] 顶栏角标判定失败: " + e.Message); return false; }
    }

    // ── 商人卡片：Show 时挂一枚，UpdateView / 任务事件 / 每秒复查 ──
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

    // ── 顶栏：任务栏建好时挂一个看守，它自己去找昵称文字并把角标贴在后面；只在主菜单亮（09-12 实机：人物屏的昵称也被贴上了）──
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

    internal static Image Build(RectTransform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = Sprite;
        img.color = _fallback ? Gold : Color.white;   // 1.1 原件自带金色和描边，原样画
        img.raycastTarget = false;
        img.preserveAspect = true;
        go.SetActive(false);
        return img;
    }
}

/// <summary>商人卡片上的那枚：右上角、随卡片宽度定尺寸（1.1 截图量的比例：角标约卡宽 12%，中心离右上角 (0.17w, 0.15w)）</summary>
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

    public void Refresh()
    {
        if (_img == null) return;
        var on = TraderBadge.Wanted(_traderId, _qc);
        if (_img.gameObject.activeSelf != on) TalkButton.Retint();   // 09-13 审查：定时在商人屏开着时到点，访问页签也得跟着变金/还原
        if (on)
        {
            var w = ((RectTransform)transform).rect.width;
            if (w <= 1f) w = 150f;
            // 09-12 第 3 轮按正式版对照图逐像素量：1.1 的角标和卡片左上的等级框一样高（≈ 卡宽 13%，实心），中心离右上角 (0.18w, 0.105w)；
            // visit_icon.png 32×30 里实心只有 24×23（四边各 4 像素透明），所以画 32% 卡宽才能实心 24%——比 1.1 略大一点，SORA 第 2 轮嫌小
            // 09-12 第 7 轮：两张单卡截图逐像素对（1.1 卡外框 161 宽，我们 158）——1.1 角标实心 20×18 = 0.124w，右边离外框右缘 0.106w、上边离外框上缘 0.11w；
            // 我们上一轮实心 24×23、低了 5 像素。现在尺寸和边距全部按 1.1 的数，不再放大（SORA 第 4 轮嫌小的那版是位置错着看的）。
            var rt = (RectTransform)_img.transform;
            const float visible = 0.15f;   // 第 8 轮 SORA「放大一点」：1.1 的 0.124w → 0.15w（大两成），右/上边距不动，大出来的往卡片里长
            var size = Mathf.Clamp(w * visible * TraderBadge.PaddingScale, 16f, 64f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(-w * (0.106f + visible / 2f), -w * (0.11f + visible / 2f));
            rt.SetAsLastSibling();
        }
        if (_img.gameObject.activeSelf != on) _img.gameObject.SetActive(on);
    }

    void LateUpdate()
    {
        if (_img == null) return;
        if (ChapterEvents.Changed(ref _seen) || Time.unscaledTime >= _next) { _next = Time.unscaledTime + 1f; Refresh(); }
    }
}

/// <summary>顶栏那枚：只在主菜单屏亮。找显示玩家昵称的文字（同名文字取字号最大的那个——MenuOverhaul 之类会自己画顶栏，不认死某个类），
/// 贴在文字右边；任一商人有话要说就亮。每 0.5 秒看一次，换屏 / 文字对象没了就重找。</summary>
public class HeaderBadge : MonoBehaviour
{
    public static EEftScreenType Screen = EEftScreenType.MainMenu;
    TMP_Text _label;
    Image _img;
    EEftScreenType _seenScreen;
    float _next, _retry;   // _retry：找不到昵称文字时的退避（09-13 审查：原来每半秒扫一遍全部 TMP 文字，没昵称的菜单会一直扫）

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
        if (_seenScreen != Screen) { _seenScreen = Screen; _label = null; _retry = 0f; }   // 回到主菜单：重找一次（换屏后文字对象常常是新建的）
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
        var size = Mathf.Clamp(_label.fontSize * 1.1f * TraderBadge.PaddingScale, 24f, 64f);   // 1.1 顶栏那枚实心约 0.9 倍字号，SORA 第 5 轮嫌小 → 1.1 倍；原件四周有透明边
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
