using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ChatShared;
using Diz.Binding;
using EFT;
using EFT.Communications;
using EFT.Quests;
using EFT.UI;
using EFT.UI.Chat;
using HarmonyLib;
using TMPro;
using UI.InfoWindow;
using UnityEngine;
using UnityEngine.UI;

namespace VisitAPI.Native;

public class PendingInvite
{
    public Quest Quest;
    public QuestFlags.Mail Mail;
    public string QuestId => Quest.Id;
    public string Trader => Mail.From;
    /// 按下按钮要开的对话属于哪家：mail 里写了 dialogueTrader 就是那家，没写就是发信的商人
    public string DialogueTrader => string.IsNullOrEmpty(Mail.DialogueTrader) ? Trader : Mail.DialogueTrader;
    public string TextKey => string.IsNullOrEmpty(Mail.TextKey) ? Quest.Id + InviteState.MailKeySuffix : Mail.TextKey;
}

/// <summary>1.1 的对话邀请（09-24 照 SORA 给的 1.1 截图重做外观）：任务可接 → 该商人往聊天里发一封信；聊天列表条目右下角挂金色电话角标（顶掉原生的未读数），
/// 不显示信文预览；信头是一条从左黑到右金的渐变横条盖在消息面板的标题行上：「↰ 商人名 / 信文」+ 右侧「📞 回复」+ 红色 ×（关聊天窗）；
/// 信文本身在消息区里不显示（1.1 里邀请不是一条消息）；底部「无法向该用户发送消息」也不显示；列表抬头换成「PMC 频段 / 特殊通讯」两个页签：
/// PMC 频段 = 玩家之间的对话，特殊通讯 = 商人 / 系统 / 跳蚤这些非玩家的（SORA 09-24：商人属于特殊通讯），浅色是当前页（1.1 / 0.16 页签都是亮的那个是选中）。</summary>
public static class InviteState
{
    internal const string MailKeySuffix = " whileAvailableMessageText";
    static float _at = -1f;
    static List<PendingInvite> _cache = new();

    public static List<PendingInvite> Pending(bool force = false)
    {
        if (!force && _at >= 0f && Time.unscaledTime - _at < 0.5f) return _cache;
        _at = Time.unscaledTime;
        var list = new List<PendingInvite>();
        try
        {
            var qc = QuestOps.Current;
            if (qc?.Quests == null) return _cache = list;
            foreach (var q in qc.Quests)
            {
                if (q?.Template == null || q.QuestStatus != EQuestStatus.AvailableForStart) continue;
                var mail = QuestFlags.MailOf(q.Id);
                if (mail == null || QuestFlags.AutoStart(q.Id)) continue;
                if (!ChapterChain.Reachable(qc, q)) continue;
                list.Add(new PendingInvite { Quest = q, Mail = mail });
            }
        }
        catch (Exception e) { Plugin.Log.LogWarning("[invite] Pending invite scan failed: " + e.Message); }
        return _cache = list;
    }

    public static List<PendingInvite> For(string traderId) =>
        string.IsNullOrEmpty(traderId) ? new List<PendingInvite>() : Pending().Where(p => string.Equals(p.Trader, traderId, StringComparison.OrdinalIgnoreCase)).ToList();

    /// 聊天列表每个条目每 0.5 秒问一次，直接在缓存里找、不先建 List
    public static bool Has(string traderId) =>
        !string.IsNullOrEmpty(traderId) && Pending().Any(p => string.Equals(p.Trader, traderId, StringComparison.OrdinalIgnoreCase));

    /// 「现在接不通」的提示（Act 和 InviteHideout.Fail 共用）
    public static string CannotConnect => Loc.Pick("现在接不通，看日志里 [invite] 的原因", "Cannot connect right now, see [invite] in the log");

    /// 关掉聊天窗再去开对话 / 访问（三个入口共用）
    public static void CloseChat(ChatScreen screen)
    {
        try { screen?.Close(); }
        catch (Exception e) { Plugin.Log.LogWarning("[invite] Failed to close chat window: " + e.Message); }
    }

    /// 服务端寄邀请信用的文案键就是消息的 templateId（QuestId + " whileAvailableMessageText"）——凭这个认出邀请信，不看是否还在待处理
    public static bool IsInviteMessage(DialogueChatMessage m) =>
        m != null && !string.IsNullOrEmpty(m.templateId) && m.templateId.EndsWith(MailKeySuffix, StringComparison.Ordinal);

    /// 1.1 的文案表里只有 InLobby / InRaid / ViaRadio 三个入口键（09-24 对着 SPT5 文案表核过），笔记本入口在 1.1 截图里也显示「回复」
    public static string ButtonLabel(string entry) => entry switch
    {
        "ViaRadio" or "ViaNotebook" => Loc.Text("Chat/DialogueInvitation/StartMethod/ViaRadio", "回复", "REPLY"),
        "InRaid" => Loc.Text("Chat/DialogueInvitation/StartMethod/InRaid", "访问", "VISIT"),
        _ => Loc.Text("Chat/DialogueInvitation/StartMethod/InLobby", "访问", "VISIT"),
    };

    public static void Act(PendingInvite p, ChatScreen screen)
    {
        var entry = p.Mail.Entry ?? "InLobby";
        var trader = p.DialogueTrader;
        if (entry == "InLobby")
        {
            if (!NarrateEntry.CanVisit(trader))
            {
                Plugin.Log.LogWarning($"[invite] Trader {trader} cannot be visited right now (room pack not installed, or dialogue not unlocked yet)");
                NotificationManager.DisplayMessageNotification(Loc.Text("Chat/DialogueInvitation/StartMethod/InRaid/Description", "你必须访问商人所在之处。", "You must visit the trader on the location."));
                return;
            }
            CloseChat(screen);
            NarrateEntry.Visit(trader);
            return;
        }
        // 10-15：对话商人有 .dlg 且剧本里挂着这条任务的藏身处触发线 → 进藏身处、传到那条线前、触发它（InviteHideout）；别的照旧
        if (InviteHideout.TryStart(p, screen)) return;
        // 09-24 第三步：笔记本 / 电台入口直接开原生对话屏放 1.1 的对话（InviteDialog），语音字幕由 InvitePresenter 顶上
        if (!InviteDialog.Open(p, screen)) NotificationManager.DisplayMessageNotification(CannotConnect);
    }
}

/// 09-24 审查 H1：待处理的邀请每 60 秒再请求一次。服务端只在档案里这个商人的对话里已经没有这封信时才补寄（玩家右键删掉了对话），
/// 信还在就直接返回；本会话只请求一次的话，删掉对话后要重开游戏才可能再收到
public static class InviteWatcher
{
    const float RequestEvery = 60f;
    static readonly Dictionary<string, float> _requestedAt = new(StringComparer.Ordinal);
    static bool _running;

    public static void Start()
    {
        if (_running) return;
        _running = true;
        Plugin.Instance.StartCoroutine(Loop());
    }

    static IEnumerator Loop()
    {
        var wait = new WaitForSecondsRealtime(2f);
        while (true)
        {
            yield return wait;
            // 09-26 F3：只在大厅（含藏身处、访问）里请求寄信。战局、撤离结算、结算页、转移途中没有大厅控制器，
            // 这时还按战局那份任务书每 60 秒 POST 一次的话，服务端收到就存档，会和撤离结算同时改档案
            if (QuestOps.Lobby == null) continue;
            var pending = InviteState.Pending(force: true);
            var now = Time.unscaledTime;
            foreach (var p in pending)
            {
                if (_requestedAt.TryGetValue(p.QuestId, out var at) && now - at < RequestEvery) continue;
                _requestedAt[p.QuestId] = now;
                VisitHttp.Post("/visitapi/mail/invite", "{\"questId\":\"" + p.QuestId + "\"}", "[invite]");
            }
        }
    }
}

/// 聊天窗用的程序生成小贴图：只有窗底的斜纹（页签、关闭盒、悬停块、「收取全部」栏用的是 1.1 原图，见 Chat11 / Client\art\chat11_*.png）
public static class ChatArt
{
    static Sprite _hatch;

    /// 1.1 聊天窗的底子是一层很淡的深色斜纹（截图上亮度 19～25 周期 4 的条纹），平铺一张 8×8 的小纹理盖在窗底上
    public static Sprite Hatch()
    {
        if (_hatch != null) return _hatch;
        const int n = 8;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
        var px = new Color32[n * n];
        for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
                px[y * n + x] = ((x + y) % 4) == 0 ? new Color32(0xFF, 0xFF, 0xFF, 0x12) : new Color32(0, 0, 0, 0);
        tex.SetPixels32(px); tex.Apply();
        return _hatch = Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
    }
}

/// 列表顶上 1.1 的两个页签（0.16.9 的聊天窗没有「行动人员」抬头，页签条挂在列表容器上方）：「PMC 频段」= 玩家 / 群聊，「特殊通讯」= 商人 / 系统 / 跳蚤。
/// 09-25 按 1.1MCP 导出的 1.1 结构重做：页签条高 34、底边比列表顶边高 3；两页各 (宽-4)/2（390 宽时 193）、中间隔 4；
/// 底图平时 / 悬停 / 选中 = Social_Tab_Backgrounds_2 / _1 / _0（四周各比页签多 4），图标 30×30 在 x 4，文字 Bender Normal 16 粗 #C7D3D9 在 x 44，三种状态文字颜色不变。
/// 聊天窗 Show 只挂这一个后缀，按先后做页签、信头、皮肤
[HarmonyPatch(typeof(ChatScreen), nameof(ChatScreen.Show))]
public static class ChatInviteTabs
{
    const float TabHeight = 34f;
    const float Lift = 3f;
    const float Gap = 4f;
    public static ChatScreen Screen;
    public static bool Special = true;   // 当前页：true = 特殊通讯，false = PMC 频段。默认开在特殊通讯（单机里有东西的是商人那边）
    static GameObject _bar;
    static TabParts _pmc, _special;
    static BindableList<UpdatableChatDialogue> _filtered;
    static float _next;
    // _social / toggleGroup_0 是 private，没在公开化范围里；反射只查一次
    static readonly FieldInfo SocialField = AccessTools.Field(typeof(DialoguesContainer), "_social");
    static readonly FieldInfo ToggleGroupField = AccessTools.Field(typeof(DialoguesContainer), "toggleGroup_0");

    class TabParts { public Image Bg; public Chat11TabHover Hover; }

    static readonly Color TabText = new Color32(0xC7, 0xD3, 0xD9, 0xFF);

    public static bool IsSpecial(UpdatableChatDialogue d) => d != null && IsSpecialType(d.Type);

    public static bool IsSpecialType(EMessageType t) => t != EMessageType.UserMessage && t != EMessageType.GroupChatMessage && t != EMessageType.GlobalChat;

    static SocialNetwork Social(DialoguesContainer dc) => dc != null ? SocialField?.GetValue(dc) as SocialNetwork : null;

    static void Postfix(ChatScreen __instance)
    {
        Screen = __instance;
        try { Build(__instance); Rebuild(); }
        catch (Exception e) { Plugin.Log.LogError("[invite] Failed to attach chat tabs (chat is unaffected): " + e); }
        try { InviteHeader.Ensure(__instance).Select(null, null); }
        catch (Exception e) { Plugin.Log.LogWarning("[invite] Failed to attach invite header: " + e.Message); }
        try { ChatSkin.Apply(__instance); }
        catch (Exception e) { Plugin.Log.LogWarning("[invite] Chat window outline failed: " + e.Message); }
    }

    static void Build(ChatScreen screen)
    {
        if (_bar != null) { _bar.SetActive(true); Style(); return; }
        var font = Chat11.Font("Bender Normal", screen);
        var want = "Chat/PMCDialoguesHeaderLabel".Localized();
        var old = screen.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t != null && !string.IsNullOrEmpty(want) && string.Equals(t.text?.Trim(), want, StringComparison.OrdinalIgnoreCase));
        if (old != null) old.gameObject.SetActive(false);
        var parent = (RectTransform)screen._dialoguesContainer.transform;
        // 1.1 的 Tabs：pos(0,3) 高 34
        var rt = Chat11.Rect(parent, "VisitChatTabs", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, Lift), new Vector2(0f, TabHeight), ignoreLayout: true);
        _bar = rt.gameObject;
        rt.SetAsLastSibling();
        _pmc = Tab(rt, "PmcTab", 0f, 0.5f, 0f, -Gap / 2f, Loc.Text("Chat/PMCFrequency", "PMC 频段", "PMC frequency"), "chat11_tab_icon_pmc.png", font, () => Select(false));
        _special = Tab(rt, "SpecialTab", 0.5f, 1f, Gap / 2f, 0f, Loc.Text("Chat/SpecialCommunications", "特殊通讯", "Special comms"), "chat11_tab_icon_special.png", font, () => Select(true));
        Style();
    }

    static TabParts Tab(RectTransform bar, string name, float x0, float x1, float left, float right, string text, string iconFile, TMP_FontAsset font, Action onClick)
    {
        var rt = Chat11.Span(bar, name, new Vector2(x0, 0f), new Vector2(x1, 1f), new Vector2(left, 0f), new Vector2(right, 0f), false, typeof(Image), typeof(Button));
        var go = rt.gameObject;
        var hit = go.GetComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f); hit.raycastTarget = true;   // 只接点击，画面由 Background 负责
        var button = go.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => onClick());
        var bg = Chat11.Fill(rt, "Background", VisitArt.Load("chat11_tab_normal.png"), Color.white, new Vector2(-4f, -4f), new Vector2(4f, 4f));

        Chat11.Img(rt, "Icon", VisitArt.Load(iconFile), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(4f, 0f), new Vector2(30f, 30f));

        var lrt = Chat11.Span(rt, "Label", new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(44f, 0f), new Vector2(-4f, 0f));
        var label = lrt.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text; label.font = font; label.fontSize = 16f; label.fontStyle = FontStyles.Bold; label.alignment = TextAlignmentOptions.Left; label.raycastTarget = false;
        label.overflowMode = TextOverflowModes.Ellipsis; label.enableWordWrapping = false; label.color = TabText;
        label.enableAutoSizing = true; label.fontSizeMin = 12f; label.fontSizeMax = 16f;

        var hover = go.AddComponent<Chat11TabHover>();
        hover.Changed = Style;
        return new TabParts { Bg = bg, Hover = hover };
    }

    static void Style()
    {
        if (_pmc == null) return;
        Style(_pmc, !Special);
        Style(_special, Special);
    }

    static void Style(TabParts t, bool selected) =>
        t.Bg.sprite = VisitArt.Load(selected ? "chat11_tab_selected.png" : t.Hover != null && t.Hover.Hovered ? "chat11_tab_hover.png" : "chat11_tab_normal.png");
    static void Select(bool special)
    {
        if (Special == special) return;
        Special = special;
        Style();
        try { Rebuild(); }
        catch (Exception e) { Plugin.Log.LogError("[invite] Chat list switch failed: " + e); }
    }

    /// 按当前页签过滤列表：换数据源重调 LightScroller.Show（转换器返回 false 不安全——ConvertedBindableList 删项时 _index[item] 会炸）
    static void Rebuild()
    {
        var dc = Screen?._dialoguesContainer;
        var social = Social(dc);
        if (social == null) return;
        var toggleGroup = ToggleGroupField?.GetValue(dc) as ToggleGroup;
        _filtered = new BindableList<UpdatableChatDialogue>();
        foreach (var d in social.Dialogues) if (d != null && IsSpecial(d) == Special) _filtered.Add(d);
        var converted = new ConvertedBindableList<UpdatableChatDialogue, DialogueData>(_filtered, dc.DialogueDataFromDialogue, DialogueData.Comparer);
        dc._scroller.Show(converted, (DialogueData d) => dc._cellViewPrefab, (DialogueData d) => (Enum)DialoguesContainer.EDialogType.None, (DialogueData item, DialogueView view) => view.Show(item, toggleGroup));
        dc._noDialogsPlaceholder.SetActive(_filtered.Count == 0);
    }

    public static void Tick()
    {
        if (_filtered == null || Screen == null || !Screen.gameObject.activeInHierarchy) return;
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 1f;
        var social = Social(Screen._dialoguesContainer);
        if (social == null) return;
        var want = social.Dialogues.Where(d => d != null && IsSpecial(d) == Special).ToList();
        if (want.Count == _filtered.Count && want.All(_filtered.Contains))
        {
            // 原生把「没有对话」占位绑在全量列表上：别的页签来了信会把它藏掉，这页空着也不显示；按本页的数补回来（10-29）
            var ph = Screen._dialoguesContainer?._noDialogsPlaceholder;
            if (ph != null && ph.activeSelf != (_filtered.Count == 0)) ph.SetActive(_filtered.Count == 0);
            return;
        }
        try { Rebuild(); }
        catch (Exception e) { Plugin.Log.LogWarning("[invite] Chat list refresh failed: " + e.Message); }
    }
}

/// 09-25 SORA 的 1.1 截图：列表里的消息预览末尾是一个「…」。逐条数过：英文正好 28 个字符（"Good stuff. Even if you don'…"），
/// 中文全是 14 个字（「那个线人在车行附近开了个会，…」「你好，雇佣兵。我听说你想要增…」等 6 条，全角标点也算一个字）——
/// 1.1 按「中文 / 全角算 2、其余算 1，满 28」截。原生 SetLastMessage 按 30 个字符截再加 "..."，中文就顶出列表右沿（文字框宽度不受限，靠遮罩切掉）。
/// 原生写完后照 1.1 的规则重截
[HarmonyPatch(typeof(DialogueView), nameof(DialogueView.SetLastMessage))]
public static class ChatPreviewEllipsis
{
    const int Budget = 28;

    static bool Wide(char c) =>
        (c >= 0x1100 && c <= 0x115F) || (c >= 0x2E80 && c <= 0xA4CF) || (c >= 0xAC00 && c <= 0xD7A3) ||
        (c >= 0xF900 && c <= 0xFAFF) || (c >= 0xFE30 && c <= 0xFE4F) || (c >= 0xFF00 && c <= 0xFF60) || (c >= 0xFFE0 && c <= 0xFFE6);

    static string Cut(string s)
    {
        var used = 0;
        for (var i = 0; i < s.Length; i++)
        {
            used += Wide(s[i]) ? 2 : 1;
            if (used > Budget) return s.Substring(0, i) + "…";
        }
        return s;
    }

    static void Postfix(DialogueView __instance, DialogueChatMessage message, UpdatableChatDialogue ____dialogue, UpdatableChatMember ____playerProfileMember)
    {
        try
        {
            var label = __instance._lastMessageLabel;
            if (label == null || message == null || ____dialogue == null) return;
            if (!(____dialogue.DeleteTime.GetValueOrDefault(DateTime.MinValue) < message.UtcDateTime)) return;   // 原生这时写的是空串
            var start = string.Empty;
            if (message.Type != EMessageType.SystemMessage)
                start = message.Member == ____playerProfileMember ? "You: ".Localized() + message.Text : message.Text;
            var full = message.ParsedText(start, EViewRule.MessageHeader);
            if (string.IsNullOrEmpty(full)) return;
            label.enableWordWrapping = false;
            label.text = Cut(full.Replace("\r", " ").Replace('\n', ' '));
        }
        catch (Exception e) { Plugin.WarnOnce("invite/preview", "[invite] Chat list preview could not be shortened: " + e.Message); }
    }
}

/// 聊天列表条目：有待处理邀请的商人，右下角金色电话角标顶掉原生的未读数（1.1 就长这样），信文预览不显示
[HarmonyPatch(typeof(DialogueView), nameof(DialogueView.Show))]
public static class ChatInviteBadge
{
    static void Postfix(DialogueView __instance, DialogueData data)
    {
        try { InviteBadge.Attach(__instance, data?.Dialogue); }
        catch (Exception e) { Plugin.Log.LogWarning("[invite] Failed to attach chat badge: " + e.Message); }
    }
}

public class InviteBadge : MonoBehaviour
{
    DialogueView _view;
    UpdatableChatDialogue _dialogue;
    Image _img;
    float _next;

    public static void Attach(DialogueView view, UpdatableChatDialogue dialogue)
    {
        TweakNative(view);
        var b = view.GetComponent<InviteBadge>() ?? view.gameObject.AddComponent<InviteBadge>();
        b._view = view;
        b._dialogue = dialogue;
        b._next = 0f;
        b.Refresh();
    }

    static readonly HashSet<int> _tweaked = new();

    /// 12:30 对照图里条目内 1.1 和 0.16.9 原生的两处差别：时间戳坐在名字的基线上（原生高了 5 单位，顶在右上角），名字离人形图标远 5 单位。条目是池化复用的，每个只调一次
    static void TweakNative(DialogueView view)
    {
        if (view == null || !_tweaked.Add(view.GetInstanceID())) return;
        try
        {
            if (view._timeStamp != null && view._timeStamp.transform is RectTransform t) t.anchoredPosition += new Vector2(-3.5f, -4f);   // 12:45 复量：下移 5 多了 2 像素改 4；左挪 5 又多了 2 像素（39.png 量右沿离条目右边 29，1.1 是 26.5）改 3.5
            if (view._playerNameLabel != null && view._playerNameLabel.transform is RectTransform n) n.offsetMin += new Vector2(5f, 0f);
            // 09-25 放大对照 SORA 的 1.1 截图（1.png / 3.png vs 11.png / 12.png）：1.1 的商人头像 40 单位见方、左沿离列表框 10、顶离条目顶 6.5；
            // 我们是原生的 30.5 见方、16 / 11——四条边各自外扩：左 6、上 4.5、右 3.5、下 5
            if (view._dialogueIcon != null && view._dialogueIcon.transform is RectTransform icon)
            {
                icon.offsetMin += new Vector2(-6f, -5f);
                icon.offsetMax += new Vector2(3.5f, 4.5f);
            }
        }
        catch (Exception e) { Plugin.WarnOnce("invite/entry-tweak", "[invite] Chat list entry could not be restyled: " + e.Message); }
    }

    void Update()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.5f;
        Refresh();
    }

    void Refresh()
    {
        var on = _dialogue != null && InviteState.Has(_dialogue._id);
        if (!on) { if (_img != null) _img.gameObject.SetActive(false); return; }
        if (_img == null) Build();
        _img.gameObject.SetActive(true);
        try
        {
            if (_view._newMessagesObject != null && _view._newMessagesObject.activeSelf) _view._newMessagesObject.SetActive(false);
            if (_view._newAttachmentsMessagesObject != null && _view._newAttachmentsMessagesObject.activeSelf) _view._newAttachmentsMessagesObject.SetActive(false);
            if (_view._lastMessageLabel != null && InviteState.IsInviteMessage(_dialogue.Message) && !string.IsNullOrEmpty(_view._lastMessageLabel.text)) _view._lastMessageLabel.text = string.Empty;
        }
        catch (Exception e) { Plugin.WarnOnce("invite/entry-cleanup", "[invite] Chat list entry cleanup failed: " + e.Message); }
    }

    void Build()
    {
        // 09-24 实机：条目根上有布局组，新挂的子物体会被它排到头像后面——加 ignoreLayout 才能按锚点落到右下角
        var host = _view._backgroundImage != null ? _view._backgroundImage.transform : transform;
        // 09-25 1.1MCP 导出：1.1 的 DialogueInvitation 角标 24×24（贴图 Social_Trader_Chat_Full-Call-Icon (2)_1），右沿离条目右边 5、底边离条目底 4.5
        _img = Chat11.Img(host, "VisitInviteBadge", VisitArt.Load("chat11_list_call.png"), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-5f, 4.5f), new Vector2(24f, 24f), ignoreLayout: true);
        _img.rectTransform.SetAsLastSibling();
    }
}

/// 邀请信头：1.1 的样子——一条从左黑到右金的渐变横条盖在消息面板的标题行（商人名 / 好友数 / ×）上并往下伸一点：
/// 「↰ 商人名」+ 信文两行，右侧「📞 回复」按钮，最右红色 ×（和 1.1 一样是关聊天窗）。单例挂在聊天窗上，跟着当前选中的对话走。
public static class ChatInviteHeader
{
    static readonly FieldInfo Containers = AccessTools.Field(typeof(ChatScreen), "_instantiatedContainers");

    // 聊天窗 Show 时挂信头 / 套皮肤在 ChatInviteTabs 的后缀里

    [HarmonyPatch(typeof(ChatScreen), nameof(ChatScreen.DialogueSelected))]
    public static class OnSelected
    {
        static void Postfix(ChatScreen __instance, UpdatableChatDialogue dialogue)
        {
            try
            {
                MessagesContainer container = null;
                if (Containers?.GetValue(__instance) is Dictionary<UpdatableChatDialogue, MessagesContainer> dict && dialogue != null) dict.TryGetValue(dialogue, out container);
                InviteHeader.Ensure(__instance).Select(dialogue, container);
            }
            catch (Exception e) { Plugin.Log.LogWarning("[invite] Invite header switch failed: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(ChatScreen), nameof(ChatScreen.OnSelectedDialogChanged))]
    public static class OnChanged
    {
        static void Postfix(ChatScreen __instance, UpdatableChatDialogue dialog)
        {
            if (dialog != null) return;
            try { InviteHeader.Ensure(__instance).Select(null, null); }
            catch (Exception e) { Plugin.Log.LogWarning("[invite] Invite header collapse failed: " + e.Message); }
        }
    }
}

/// 09-24：聊天窗往 1.1 靠——12:30 按 SORA 给的 1.1 / SPT 同分辨率对照图重量：0.16.9 自带的窗框灰线（#585D60）1.1 也有、位置一样，
/// 不用另加边线（会叠出 2 像素粗边）；1.1 多的是列表和消息区之间那条通高的分割线，补上；整窗只是列表加宽 90（右栏尺寸不变）
public static class ChatSkin
{
    static readonly HashSet<int> _done = new();

    public static void Apply(ChatScreen screen)
    {
        if (screen == null || !_done.Add(screen.GetInstanceID())) return;
        var root = screen.RectTransform;
        if (root == null) return;
        MoveFriendsButton(screen);
        try { Resize(screen, first: true); }
        catch (Exception e) { Plugin.Log.LogWarning("[invite] Chat window resize failed: " + e); }
        try { SplitLine(screen); }
        catch (Exception e) { Plugin.Log.LogWarning("[invite] Chat window divider failed: " + e.Message); }
        try { HatchBackground(root); }
        catch (Exception e) { Plugin.Log.LogWarning("[invite] Chat window hatch background failed: " + e.Message); }
    }

    // 12:30 对照图（2560×1440，1 单位 = 1.333 像素）量的 1.1 聊天窗：整窗 1522 像素 = 1141 单位，列表 520 像素 = 390，分割线 1 像素，右栏 998 像素 = 750，高 602 像素 = 450
    // ——就是 0.16.9 原生（1052 / 300 / 750 / 450）只把列表加宽 90，右栏一点没动
    // 12:55 日志核实：聊天窗根是 HorizontalLayoutGroup（spacing 2）+ ContentSizeFitter，列表和右栏各带 LayoutElement——两栏之间那 2 单位露的是根的 #222222 底，不是右栏的边；
    // 1.1 的 1141.5 = 390 + 1 + 750：把 spacing 改成 1，分割线（1 单位）正好填满这道缝，整窗 1141
    public const float Gap = 1f;
    const float TargetW = 1141f, TargetList = 390f;

    static RectTransform RightPanel(ChatScreen screen) =>
        screen._captionPanel != null ? screen._captionPanel.transform.parent as RectTransform : null;

    static RectTransform ListPanel(ChatScreen screen) =>
        screen._dialoguesContainer != null ? screen._dialoguesContainer.transform as RectTransform : null;

    /// 聊天窗是个 StretchWindow（根下的 StretchButtons 能拖着改大小）：尺寸由 LayoutElement 的 preferredWidth/Height 经布局系统驱动，
    /// 直接写 sizeDelta 下一次布局重排就被冲回去（12:20 截图窗体还是 1052 宽、日志里却记着改成了 1371，就是这么回事）——所以改 preferred 值，再强制重排一次
    public static bool Resize(ChatScreen screen, bool first)
    {
        var root = screen.RectTransform;
        var list = ListPanel(screen);
        var right = RightPanel(screen);
        if (root == null || list == null || right == null) { if (first) Plugin.Log.LogWarning("[invite] Chat window resize: missing panel references, skipping"); return false; }
        var old = root.rect.size;
        var sw = root.GetComponentInChildren<StretchWindow>(true);
        var le = sw != null && sw._stretchableObject != null ? sw._stretchableObject : root.GetComponent<LayoutElement>();
        if (le != null && le.preferredWidth < TargetW) le.preferredWidth = TargetW;   // 只加宽；高度按原生 450 不动
        if (sw != null && sw._maxSize.x < TargetW) sw._maxSize = new Vector2(TargetW, sw._maxSize.y);
        if (root.rect.width < TargetW - 0.5f) root.sizeDelta = new Vector2(TargetW, root.sizeDelta.y);
        if (first) root.anchoredPosition -= new Vector2((TargetW - old.x) * (0.5f - root.pivot.x), 0f);   // 中心不动
        var lle = list.GetComponent<LayoutElement>();
        if (lle != null)
        {
            if (lle.preferredWidth < TargetList) lle.preferredWidth = TargetList;
            if (lle.minWidth > 0f && lle.minWidth < TargetList) lle.minWidth = TargetList;
        }
        // 12:55 的日志已经核实聊天窗根就是 HorizontalLayoutGroup，右栏由它排
        var group = root.GetComponent<HorizontalOrVerticalLayoutGroup>();
        if (group != null) group.spacing = Gap;   // 原生 2；1.1 两栏之间只有 1 像素灰线
        if (Mathf.Approximately(list.anchorMin.x, list.anchorMax.x)) list.sizeDelta = new Vector2(TargetList, list.sizeDelta.y);
        LayoutRebuilder.ForceRebuildLayoutImmediate(root);
        return root.rect.width >= TargetW - 1f && list.rect.width >= TargetList - 1f;
    }

    /// 每半秒看一眼（InviteHeader.Update 里调）：尺寸被原生布局冲回去了就再套一次，只在窗体退回原生 1052 宽时才动手，用户自己拖小的不管
    public static void Keep(ChatScreen screen)
    {
        if (screen == null || !_done.Contains(screen.GetInstanceID())) return;
        var root = screen.RectTransform;
        var list = ListPanel(screen);
        if (root == null || list == null) return;
        var rootBack = Mathf.Abs(root.rect.width - 1052f) < 1f;
        var listBack = Mathf.Abs(list.rect.width - 300f) < 1f;
        if (!rootBack && !listBack) return;
        if (!Resize(screen, first: false)) Plugin.WarnOnce("invite/resize", "[invite] Chat window was reset to its native size and could not be widened back to the 1.1 size");
    }

    /// 1.1 列表和消息区之间那条通高的 1 像素灰线（#585D60，对照图 x=1110 从窗框顶到底）：挂在列表容器右沿外侧，正好填满布局组留的那 Gap 单位缝
    static void SplitLine(ChatScreen screen)
    {
        var list = ListPanel(screen);
        if (list == null) return;
        var rt = Chat11.Span(list, "VisitSplitLine", new Vector2(1f, 0f), Vector2.one, Vector2.zero, new Vector2(Gap, 0f), true, typeof(Image));
        var img = rt.GetComponent<Image>();
        img.color = Chat11.Border; img.raycastTarget = false;
        rt.SetAsLastSibling();
    }

    /// 1.1 聊天窗的底子是很淡的深色斜纹；平铺一层盖在窗底上、压在其它内容下面（放在第一个带 Image 的子物体之后，别被窗底图盖住）
    static void HatchBackground(RectTransform root)
    {
        var rt = Chat11.Fill(root, "VisitChatHatch", ChatArt.Hatch(), Color.white, Vector2.zero, Vector2.zero, Image.Type.Tiled).rectTransform;
        var index = 0;
        for (var i = 0; i < root.childCount; i++)
        {
            var c = root.GetChild(i);
            if (c != rt && c.GetComponent<Image>() != null) { index = i + 1; break; }
        }
        rt.SetSiblingIndex(Mathf.Clamp(index, 0, root.childCount - 1));
    }

    /// 1.1 的「好友: N」在列表面板左下角（「+」在右下角），0.16.9 放在消息面板标题行里——搬到列表左下
    static void MoveFriendsButton(ChatScreen screen)
    {
        try
        {
            var fb = screen._friendsButton; var dc = screen._dialoguesContainer;
            if (fb == null || dc == null) return;
            var frt = (RectTransform)fb.transform;
            var size = frt.rect.size;
            var le = fb.GetComponent<LayoutElement>() ?? fb.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            frt.SetParent(dc.transform, false);
            frt.anchorMin = frt.anchorMax = Vector2.zero; frt.pivot = Vector2.zero;
            frt.anchoredPosition = new Vector2(10f, 9f);   // 12:30 对照图：1.1 的「好友」左沿离列表左边 12 单位、底边离窗框 11（我们原来 10 / 10）
            frt.sizeDelta = new Vector2(Mathf.Max(size.x, 80f), Mathf.Max(size.y, 22f));
            frt.SetAsLastSibling();
        }
        catch (Exception e) { Plugin.Log.LogWarning("[invite] Failed to move friends button: " + e.Message); }
    }
}

public class InviteHeader : MonoBehaviour
{
    // 09-25 1.1MCP 导出的 1.1 CaptionPanel：高 53.61
    const float Height = 53.61f;
    ChatScreen _screen;
    UpdatableChatDialogue _dialogue;
    MessagesContainer _container;
    GameObject _panel;
    TMP_Text _name, _text, _btnLabel;
    DefaultUIButton _replyBtn;
    string _replyText;
    PendingInvite _current;
    float _next;
    RectTransform _shiftedScroller;
    Vector2 _shiftedTop;

    public static InviteHeader Ensure(ChatScreen screen)
    {
        var h = screen.GetComponent<InviteHeader>() ?? screen.gameObject.AddComponent<InviteHeader>();
        h._screen = screen;
        return h;
    }

    public void Select(UpdatableChatDialogue dialogue, MessagesContainer container)
    {
        if (_container != container) Unshift();
        _dialogue = dialogue; _container = container; _next = 0f;
        Refresh();
    }

    void Update()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.5f;
        Refresh();
        ChatInviteTabs.Tick();
        try { ChatSkin.Keep(_screen); }
        catch (Exception e) { Plugin.WarnOnce("invite/size-watchdog", "[invite] Chat window size watchdog failed: " + e.Message); }
    }

    void Refresh()
    {
        try { ApplySpecialLayout(); }
        catch (Exception e) { Plugin.WarnOnce("invite/input-area", "[invite] Input area show/hide failed: " + e.Message); }
        var live = _dialogue != null && _screen != null && _screen.gameObject.activeInHierarchy;
        if (!live) { Hide(); return; }
        var list = InviteState.For(_dialogue._id);
        if (list.Count == 0)
        {
            // 09-25 SORA 给的 1.1 截图：特殊通讯里没有邀请时，标题行也是 1.1 的样子（34.5 单位高、纯色底、↰ + 商人名 + 红 ×），不是原生 25 单位的那条
            if (!ChatInviteTabs.IsSpecial(_dialogue)) { Hide(); return; }
            if (_panel == null && !Build()) return;
            SetMode(plain: true);
            _current = null;
            _name.text = DialogueName(_dialogue);
            Place();
            _panel.SetActive(true);
            Shift();
            return;
        }
        if (_panel == null && !Build()) return;
        SetMode(plain: false);
        _current = list[0];
        _name.text = Loc.TraderName(_current.Trader);
        var text = Loc.Text(_current.TextKey, "有话要说。", "Wants to talk.");
        _text.text = list.Count > 1 ? text + $"  (+{list.Count - 1})" : text;
        var label = InviteState.ButtonLabel(_current.Mail.Entry);
        if (label != _replyText)
        {
            _replyText = label;
            if (_replyBtn != null) _replyBtn.SetRawText(label, 14);
            else if (_btnLabel != null) _btnLabel.text = label;
        }
        Place();
        _panel.SetActive(true);
        Shift();
    }

    // 09-25 按 SORA 的 1.1 截图（1.png / 3.png，2560×1440）量的「没有邀请时」标题行：从窗框顶线到自己底线 46 像素 = 34.5 单位，
    // 底色纯 #191B1B、底线 2 像素 #585D60；↰ 图形左沿离右栏左边 9.75 单位、顶离行顶 12；商人名和 × 的位置、颜色和邀请横幅相同
    const float PlainHeight = 33.61f;   // 09-25 1.1MCP 导出：没有邀请时 CaptionPanel 高 33.61（= 上 8 + 行 17.61 + 下 8）
    static readonly Color PlainBg = new Color32(0x19, 0x1B, 0x1B, 0xFF);
    bool _plain, _modeSet;
    float _height = Height;
    Image _art;
    GameObject _reply;
    RectTransform _nameRt;

    /// 有邀请 = 1.1 的 CaptionPanel：底图 Social_Trader_Chat_Hader-Background + 名字 / 信文两行 + 回复按钮；
    /// 没邀请 = 同一个 CaptionPanel 关掉底图、信文行和按钮，高度收到 33.61——图标和名字就是第一行原来的位置（离顶 8、行高 17.61，正好上下居中）
    void SetMode(bool plain)
    {
        if (_modeSet && _plain == plain) return;
        _modeSet = true;
        _plain = plain;
        _height = plain ? PlainHeight : Height;
        _art.gameObject.SetActive(!plain);
        _text.gameObject.SetActive(!plain);
        _reply.SetActive(!plain);
        // 1.1：CaptionBlock 内边距 左 8 上 8，第一行（图标 16 + 间距 5 + 名字）17.61 高，行距 3，第二行（信文）17 高；两种状态第一行位置相同（Build 里摆好），只换名字的右沿
        _nameRt.offsetMax = new Vector2(plain ? -60f : -230f, -8f);
        Unshift();   // 高度变了，消息列表顶端按新高度重让（下一次 Shift）
    }

    static string DialogueName(UpdatableChatDialogue d)
    {
        try
        {
            var n = d.Profile?.LocalizedNickname;
            if (!string.IsNullOrEmpty(n)) return n;
        }
        catch { }
        return Loc.TraderName(d._id);
    }

    Vector2? _nativeMsgMin;   // 改消息区底边之前原生的 offsetMin；回到玩家对话时还原
    GameObject _bar;
    TMP_Text _barLabel;

    // 09-25 1.1MCP 导出：1.1 的「收取全部」是原生 ReceiveAllButton 改的，750×65，平时 / 悬停 / 按下各挂一张底图 Reciewe-All_Chat-Button_0/1/2，
    // 文字「收取全部」Bender Shadowed 20 号 #E7E5D4（悬停变黑），回形针 icon_attachment（悬停换 icon_attachment_black），外框 border_generic #585D60
    const float BarHeight = 65f;

    /// 1.1 里商人 / 系统这类对话的消息区一黑到底、没有输入框（SORA 09-24：正式版特殊频道内没有对话框）。
    /// 09-25 SORA 给了 1.1 截图：有附件可领时底下也没有输入框，只有一条「收取全部」栏。所以非玩家对话一律藏掉原生发送栏，
    /// 有附件时在原来发送栏的位置底部画 1.1 那条栏（点它调原生「全部领取」），消息区往下长到栏的顶线；没附件时长到底
    void ApplySpecialLayout()
    {
        var input = _screen != null ? _screen._inputPanel : null;
        var mp = _screen != null ? _screen._messagesContainerParent : null;
        if (input == null || mp == null) return;
        var special = _dialogue != null && _screen.gameObject.activeInHierarchy && ChatInviteTabs.IsSpecial(_dialogue);
        bool rewards;
        // 09-25 SORA：明明没东西可收还显示「收取全部」。原生 AnyRewardMessage 先看服务端对话列表的 hasMessagesWithRewards，为真就直接返回真，
        // 这个标记在附件领完后不一定清（加上 MailViewRouter 把领过的信照 1.1 显示成「(已收取)」）。这里只看消息本身：
        // DisplayRewardStatus = 有奖励、没过期、还有附件或未兑现的档案事件——和消息气泡上「领取」按钮的判断一致
        try { rewards = special && _dialogue.ChatMessages != null && _dialogue.ChatMessages.Any(m => m != null && m.DisplayRewardStatus); }
        catch { rewards = special && input._receiveAllButton != null && input._receiveAllButton.gameObject.activeSelf; }
        if (input.gameObject.activeSelf == special) input.gameObject.SetActive(!special);
        var inputRt = (RectTransform)input.transform;
        FitMessagesBottom(mp, inputRt, special, rewards);
        if (!rewards) { if (_bar != null && _bar.activeSelf) _bar.SetActive(false); return; }
        if (_bar == null && !BuildBar(input)) return;
        PlaceBar(inputRt);
        if (!_bar.activeSelf) _bar.SetActive(true);
    }

    /// 09-25 SORA 实机：新消息从底下冒出来时被「收取全部」栏挡住。不按「比原生多伸长多少」记增量改消息区：原生中途重排消息区或发送栏高度一变，
    /// 记下的增量就和实际对不上。每次刷新都按世界坐标直接对齐底边（顶边不动）：特殊通讯有附件 → 底边 = 栏的顶线；特殊通讯没附件 → 底边 = 发送栏底边（一黑到底）；
    /// 其他对话 → 还原成改之前原生的底边
    void FitMessagesBottom(RectTransform mp, RectTransform inputRt, bool special, bool rewards)
    {
        if (!special)
        {
            if (_nativeMsgMin.HasValue) { mp.offsetMin = _nativeMsgMin.Value; _nativeMsgMin = null; }
            return;
        }
        var parent = mp.parent as RectTransform;
        if (parent == null) return;
        var corners = new Vector3[4];
        inputRt.GetWorldCorners(corners);
        var inBottom = parent.InverseTransformPoint(corners[0]).y;
        // 栏画在聊天窗根里、高 BarHeight 个根单位；按两者的世界缩放比换算进消息区父物体的单位
        var root = Root;
        var scale = root != null && parent.lossyScale.y > 0f ? root.lossyScale.y / parent.lossyScale.y : 1f;
        var target = rewards ? inBottom + BarHeight * scale : inBottom;
        mp.GetWorldCorners(corners);
        var cur = parent.InverseTransformPoint(corners[0]).y;
        var delta = target - cur;
        if (Mathf.Abs(delta) < 0.25f) return;
        if (!_nativeMsgMin.HasValue) _nativeMsgMin = mp.offsetMin;
        mp.offsetMin = new Vector2(mp.offsetMin.x, mp.offsetMin.y + delta);
    }

    bool BuildBar(ChatMessageSendBlock input)
    {
        var root = Root;
        if (root == null) return false;
        // 位置 / 尺寸由 PlaceBar 按原生发送栏现算，这里只定锚点 + 轴心（左下）
        var rt = Chat11.Rect(root, "VisitReceiveAllBar", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, true, typeof(Image), typeof(Button));
        _bar = rt.gameObject;
        var bg = _bar.GetComponent<Image>();
        bg.sprite = VisitArt.Load("chat11_receive_0.png"); bg.type = Image.Type.Simple; bg.color = Color.white; bg.raycastTarget = true;
        var button = _bar.GetComponent<Button>();
        button.transition = Selectable.Transition.None;   // 三态由 Chat11States 换底图，不要 Button 自己再叠一层变色
        button.onClick.AddListener(() =>
        {
            try { input._receiveAllButton.OnClick.Invoke(); }
            catch (Exception e) { Plugin.Log.LogWarning("[invite] receive-all bar: native receive-all failed: " + e.Message); }
        });
        // 回形针和文字的位置沿用照 1.1 截图量的（回形针左沿在栏中线左边 69.5、文字左沿在中线左边 37）；图标盒 22（1.1 是 22.01）
        var iimg = Chat11.Img(rt, "Icon", VisitArt.Load("chat11_attach.png"), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), new Vector2(-69.5f, 0f), new Vector2(22f, 22f));
        var lrt = Chat11.Rect(rt, "Label", new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, 0.5f), new Vector2(-37f, 0f), new Vector2(300f, 0f));
        _barLabel = lrt.gameObject.AddComponent<TextMeshProUGUI>();
        _barLabel.font = Chat11.Font("Bender Shadowed", _screen); _barLabel.fontSize = 20f; _barLabel.color = Chat11.ButtonText;
        _barLabel.alignment = TextAlignmentOptions.MidlineLeft; _barLabel.enableWordWrapping = false; _barLabel.raycastTarget = false;
        _barLabel.text = ReceiveAllText(input);
        Chat11.Fill(rt, "Border", Chat11.BorderSprite, Chat11.Border, Vector2.zero, Vector2.zero, Image.Type.Sliced);
        var states = _bar.AddComponent<Chat11States>();
        states.Target = bg; states.Normal = VisitArt.Load("chat11_receive_0.png"); states.Hover = VisitArt.Load("chat11_receive_1.png"); states.Pressed = VisitArt.Load("chat11_receive_2.png");
        states.Label = _barLabel; states.Icon = iimg; states.IconNormal = VisitArt.Load("chat11_attach.png"); states.IconHover = VisitArt.Load("chat11_attach_black.png");
        return true;
    }

    TMP_Text _receiveSrc;   // 原生「全部领取」按钮上的文字控件：找一次记住，每半秒只读它的 text

    /// 文字用原生「全部领取」按钮上的文案再查一次本地化表：0.16.9 的按钮直接写着英文 "RECEIVE ALL"（09-25 SORA 截图），
    /// 而这串字本身就是文案键——游戏的中文表里 "RECEIVE ALL" = 「收取全部」，和 1.1 一样。每次刷新都重算，中途切语言跟得上（10-17 定）
    string ReceiveAllText(ChatMessageSendBlock input)
    {
        if (_receiveSrc == null && input._receiveAllButton != null)
            _receiveSrc = input._receiveAllButton.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.text));
        var t = _receiveSrc != null ? _receiveSrc.text : null;
        var key = string.IsNullOrWhiteSpace(t) ? "RECEIVE ALL" : t.Trim();
        var s = key.Localized();
        return string.IsNullOrWhiteSpace(s) ? Loc.Pick("收取全部", "Receive all") : s;
    }

    /// 栏的底边、左右和原生发送栏对齐（按世界坐标换算进聊天窗根，窗口拖动 / 缩放都跟得上），高 BarHeight；
    /// 排在窗框 Border 之前，窗框的底线和左右线照样压在最上面
    void PlaceBar(RectTransform inputRt)
    {
        var root = Root; var rt = (RectTransform)_bar.transform;
        var corners = new Vector3[4];
        inputRt.GetWorldCorners(corners);
        var bl = root.InverseTransformPoint(corners[0]);
        var tr = root.InverseTransformPoint(corners[2]);
        var r = root.rect;
        rt.anchoredPosition = new Vector2(bl.x - r.xMin, bl.y - r.yMin);
        rt.sizeDelta = new Vector2(tr.x - bl.x, BarHeight);
        var border = root.Find("Border");
        if (border != null) rt.SetSiblingIndex(Mathf.Max(0, border.GetSiblingIndex() - (rt.GetSiblingIndex() < border.GetSiblingIndex() ? 1 : 0)));
        if (_barLabel != null && _screen != null && _screen._inputPanel != null) _barLabel.text = ReceiveAllText(_screen._inputPanel);
    }

    RectTransform Root => _screen != null ? _screen.RectTransform : null;
    RectTransform Caption => _screen != null && _screen._captionPanel != null ? _screen._captionPanel.transform as RectTransform : null;

    bool Build()
    {
        var root = Root;
        if (root == null) return false;
        var font = Chat11.Font("Bender Normal", _screen);
        // 位置 / 尺寸由 Place 按标题行现算，这里只定锚点 + 轴心（左下）；聊天窗根上若有布局组，别让它排我（ignoreLayout）
        var rt = Chat11.Rect(root, "VisitInviteHeader", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, Vector2.zero, true, typeof(Image));
        _panel = rt.gameObject;
        // 1.1 CaptionPanel 底色 #191B1B；不透明，盖住下面 0.16 原生标题行的字和按钮，关窗走自己的 ×
        var bg = _panel.GetComponent<Image>();
        bg.sprite = null; bg.type = Image.Type.Simple; bg.color = PlainBg; bg.raycastTarget = true;
        // 12:55 SORA：按住横幅拖不动窗口——原生是标题行上挂的 UIDragComponent 在拖，横幅把标题行盖住后事件到不了它；横幅自己也挂一个，拖的目标同样是聊天窗根
        try { _panel.AddComponent<UIDragComponent>().Init(root, putOnTop: true); }
        catch (Exception e) { Plugin.Log.LogWarning("[invite] Failed to attach banner dragging: " + e.Message); }
        // 1.1：Background（Social_Trader_Chat_Hader-Background）铺满 CaptionPanel、左右各多 5、上多 5、下多 3（pos(0,1) sizeDelta(10,8)）
        _art = Chat11.Fill(rt, "Background", VisitArt.Load("chat11_header_bg.png"), Color.white, new Vector2(-5f, -3f), new Vector2(5f, 5f));

        // 1.1：标题图标 Social_Header_Icons_1 16×16，左沿离左边 8
        Chat11.Img(rt, "Icon", VisitArt.Load("chat11_header_icon.png"), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(8f, -8f - 17.61f / 2f), new Vector2(16f, 16f));

        // 1.1：名字 Bender Normal 16 粗 #B6B4A5，信文 14 #C3CDD3；两行都从左边 29（8 + 图标 16 + 间距 5）开始，第二行离顶 28.61、高 17
        _name = Label(rt, "Name", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(29f, -8f - 17.61f), new Vector2(-230f, -8f), font, 16f, new Color32(0xB6, 0xB4, 0xA5, 0xFF), FontStyles.Bold);
        _nameRt = (RectTransform)_name.transform;
        _text = Label(rt, "Text", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(29f, -28.61f - 17f), new Vector2(-230f, -28.61f), font, 14f, new Color32(0xC3, 0xCD, 0xD3, 0xFF), FontStyles.Normal);
        _text.overflowMode = TextOverflowModes.Ellipsis;

        // 1.1：按钮区靠右，右内边距 15、关闭格 30 宽、间距 10 → 回复按钮右沿在 -55；按钮本身是原生 DefaultUIButton（克隆 0.16 的「收取全部」按 1.1 参数改）
        _replyBtn = Chat11.CloneButton(_screen._inputPanel != null ? _screen._inputPanel._receiveAllButton : null, rt, "Reply",
            InviteState.ButtonLabel("InLobby"), 14f, VisitArt.Load("chat11_call_idle.png"), VisitArt.Load("chat11_call_hover.png"));
        if (_replyBtn != null)
        {
            var brt = (RectTransform)_replyBtn.transform;
            brt.anchorMin = brt.anchorMax = new Vector2(1f, 0.5f); brt.pivot = new Vector2(1f, 0.5f);
            brt.anchoredPosition = new Vector2(-55f, 0f);
            _replyBtn.OnClick.AddListener(() => { if (_current != null) InviteState.Act(_current, _screen); });
            _reply = _replyBtn.gameObject;
        }
        else _reply = PlainReply(rt, font);

        // 1.1：关闭 = close_button 27×17（右沿离右边 15、竖直居中）+ 叉 close_window_x 9×11
        var crt = Chat11.Rect(rt, "Close", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-15f, 0f), new Vector2(27f, 17f), false, typeof(Image), typeof(Button));
        var cimg = crt.GetComponent<Image>();
        cimg.sprite = VisitArt.Load("chat11_close.png"); cimg.type = Image.Type.Simple; cimg.color = Color.white;
        var cbtn = crt.GetComponent<Button>();
        cbtn.transition = Selectable.Transition.None;
        cbtn.onClick.AddListener(() =>
        {
            try { if (_screen._closeButton != null) _screen._closeButton.onClick.Invoke(); else _screen.Close(); }
            catch (Exception e) { Plugin.Log.LogWarning("[invite] Failed to close chat window: " + e.Message); }
        });
        Chat11.Img(crt, "X", VisitArt.Load("chat11_close_x.png"), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(9f, 11f), preserveAspect: false);

        Chat11.Frame(rt);   // 1.1 的 Border 在 CaptionPanel 最后一个子物体，压在最上面
        return true;
    }

    /// 把横条按标题行在聊天窗根里的位置摆好：盖住标题行，再往下伸到 Height 高；每次刷新都算一遍，窗口拖动 / 缩放跟得上
    void Place()
    {
        var root = Root; var cap = Caption; var rt = (RectTransform)_panel.transform;
        if (root == null) return;
        if (cap == null)
        {
            var mp = _screen._messagesContainerParent;
            if (mp == null) return;
            cap = mp;
        }
        var corners = new Vector3[4];
        cap.GetWorldCorners(corners);
        var bl = root.InverseTransformPoint(corners[0]);
        var tr = root.InverseTransformPoint(corners[2]);
        var capH = Mathf.Max(0f, tr.y - bl.y);
        var extra = Mathf.Max(0f, _height - capH);
        var r = root.rect;
        // 横向撑满右栏（SORA 11:45 说的是高度：横幅底边要和列表里第一条商人条目下面的分隔线齐）。
        // 09-25 起灰边由自己的 Border（1.1 的 border_generic，比本体右上各多 1）画，不给窗框线让位
        rt.anchoredPosition = new Vector2(bl.x - r.xMin, bl.y - extra - r.yMin);
        rt.sizeDelta = new Vector2(tr.x - bl.x, capH + extra);
        rt.SetAsLastSibling();
    }

    void Shift()
    {
        var sc = _container?._scroller != null ? _container._scroller.transform as RectTransform : null;
        if (sc == null || _shiftedScroller == sc) return;
        Unshift();
        if (Mathf.Approximately(sc.anchorMin.y, sc.anchorMax.y)) return;
        var cap = Caption;
        var extra = _height - (cap != null ? cap.rect.height : 0f);
        if (extra <= 0f) return;
        _shiftedScroller = sc; _shiftedTop = sc.offsetMax;
        sc.offsetMax = new Vector2(_shiftedTop.x, _shiftedTop.y - extra);
    }

    void Unshift()
    {
        if (_shiftedScroller != null) _shiftedScroller.offsetMax = _shiftedTop;
        _shiftedScroller = null;
    }

    void Hide()
    {
        if (_panel != null && _panel.activeSelf) _panel.SetActive(false);
        Unshift();
    }

    /// 克隆原生按钮失败时的兜底：一个没有动画的普通按钮（电话图标 + 文字），位置和 1.1 的回复按钮一样
    GameObject PlainReply(RectTransform rt, TMP_FontAsset font)
    {
        var brt = Chat11.Rect(rt, "Reply", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-55f, 0f), new Vector2(110f, 31.4f), false, typeof(Image), typeof(Button));
        var btn = brt.gameObject;
        btn.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f);
        btn.GetComponent<Button>().onClick.AddListener(() => { if (_current != null) InviteState.Act(_current, _screen); });
        Chat11.Img(brt, "Icon", VisitArt.Load("chat11_call_idle.png"), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(23.4f, 23.4f));
        _btnLabel = Label(brt, "Label", Vector2.zero, Vector2.one, new Vector2(20f, 0f), new Vector2(-20f, 0f), Chat11.Font("Bender Shadowed", font), 14f, Chat11.ButtonText, FontStyles.Normal);
        _btnLabel.alignment = TextAlignmentOptions.Center;
        _btnLabel.text = InviteState.ButtonLabel("InLobby");
        return btn;
    }

    static TMP_Text Label(RectTransform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax, TMP_FontAsset font, float size, Color color, FontStyles style)
    {
        var t = Chat11.Span(parent, name, aMin, aMax, offMin, offMax).gameObject.AddComponent<TextMeshProUGUI>();
        t.font = font; t.fontSize = size; t.color = color; t.fontStyle = style; t.raycastTarget = false;
        t.alignment = TextAlignmentOptions.Left; t.enableWordWrapping = false; t.overflowMode = TextOverflowModes.Ellipsis;   // 1.1 的 Caption 都是 Left（左对齐、竖直居中）
        return t;
    }
}

/// 底栏「消息」按钮旁的金色电话（1.1：有待处理邀请时，未读数绿框左边亮一个金色电话 + 气泡图标；SORA 09-24 圈出来要的）。
/// 底栏 Awake 只挂这一个后缀，按先后挂这里的电话和顶栏名字旁的金色电话（HeaderBadge）
[HarmonyPatch(typeof(MenuTaskBar), "Awake")]
public static class ChatInviteTaskbar
{
    static void Postfix(MenuTaskBar __instance)
    {
        try { if (__instance.GetComponent<InviteTaskbarBadge>() == null) __instance.gameObject.AddComponent<InviteTaskbarBadge>(); }
        catch (Exception e) { Plugin.Log.LogWarning("[invite] Failed to attach bottom bar phone badge: " + e.Message); }
        try { if (__instance.GetComponent<HeaderBadge>() == null) __instance.gameObject.AddComponent<HeaderBadge>(); }
        catch (Exception e) { Plugin.Log.LogError("[badge] Failed to attach top bar badge (the task bar itself is unaffected): " + e); }
    }
}

public class InviteTaskbarBadge : MonoBehaviour
{
    MenuTaskBar _bar;
    Image _img;
    float _next;

    void Awake() { _bar = GetComponent<MenuTaskBar>(); }

    void Update()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.5f;
        try { Refresh(); }
        catch (Exception e) { Plugin.WarnOnce("invite/taskbar-badge", "[invite] Bottom bar phone badge refresh failed: " + e.Message); }
    }

    void Refresh()
    {
        var on = InviteState.Pending().Count > 0;
        if (!on) { if (_img != null) _img.gameObject.SetActive(false); return; }
        if (_img == null && !Build()) return;
        _img.gameObject.SetActive(true);
        Place();
    }

    RectTransform _badge;   // 原生未读数绿框
    RectTransform _host;    // 消息按钮的框（ChatButton 的父物体）

    // 09-25 1.1MCP 导出的 1.1 底栏：消息按钮右上方那排提示（NewInformation，右对齐、间距 4）的第一格就是电话——格子 20×20，
    // 图片 Social_Trader_Chat_Full-Call-Icon (2)_1 34×34 居中叠在格子上；那排的竖直中心在按钮底边之上 42、右沿和按钮右沿齐。
    // 所以电话中心 = (按钮右沿 - 10, 按钮底边 + 42)；绿色未读数亮着时，绿框排在电话右边，电话再往左让「绿框宽 + 4」
    const float Size = 34f;
    const float Rise = 42f;

    /// SORA 的规则：电话占绿色未读数原来的位置（消息按钮右上角）；绿框同时亮着时，电话挪到绿框左边。
    /// 绿框那个容器是布局组排的，没亮时它记的坐标不可信（11:10 那版按它摆到了按钮右边）；ChatToggle 的矩形高度是 0、父物体的矩形又比按钮图窄
    /// （12:20 截图电话离按钮右沿差了 16 单位），所以按父物体下面所有 Image 的世界矩形并集 = 按钮的可见矩形来摆
    void Place()
    {
        if (_img == null || _host == null) return;
        var rt = (RectTransform)_img.transform;
        var badgeOn = _badge != null && _badge.gameObject.activeInHierarchy;
        var shift = badgeOn ? _badge.rect.width + 4f : 0f;
        var vis = VisualRect();
        var target = new Vector2(vis.xMax - 10f - shift, vis.yMin + Rise);   // 电话中心（pivot 居中）
        rt.anchoredPosition = target - new Vector2(_host.rect.xMin, _host.rect.yMin);   // 锚在父物体左下角，直接用父物体局部坐标
    }

    Image[] _buttonImages;   // 按钮框下面算可见矩形用的那些图：找一次记住，有一张没了就重找；显隐按当时的状态算

    Rect VisualRect()
    {
        if (_buttonImages == null || _buttonImages.Any(im => im == null))
            _buttonImages = _host.GetComponentsInChildren<Image>(true)
                .Where(im => im != null && im != _img && !im.transform.IsChildOf(_img.transform) && (_badge == null || !im.transform.IsChildOf(_badge)))   // 自己和绿框不算按钮
                .ToArray();
        var min = new Vector2(float.MaxValue, float.MaxValue); var max = new Vector2(float.MinValue, float.MinValue); var any = false;
        var corners = new Vector3[4];
        foreach (var im in _buttonImages)
        {
            if (!im.gameObject.activeInHierarchy) continue;
            ((RectTransform)im.transform).GetWorldCorners(corners);
            for (var i = 0; i < 4; i++) { var p = (Vector2)_host.InverseTransformPoint(corners[i]); min = Vector2.Min(min, p); max = Vector2.Max(max, p); any = true; }
        }
        return any ? Rect.MinMaxRect(min.x, min.y, max.x, max.y) : _host.rect;
    }

    bool Build()
    {
        if (_bar == null) return false;
        _badge = _bar._newMessagesObject != null ? _bar._newMessagesObject.transform as RectTransform : null;
        try
        {
            var toggle = _bar.ChatToggle != null ? _bar.ChatToggle.transform as RectTransform : null;
            _host = toggle != null && toggle.rect.height < 1f && toggle.parent is RectTransform p ? p : toggle;
        }
        catch { _host = null; }
        if (_host == null) return false;
        // 锚在父物体左下角，Place 里按按钮可见矩形算电话中心
        _img = Chat11.Img(_host, "VisitInviteTaskbarBadge", VisitArt.Load("chat11_list_call.png"), Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(Size, Size), ignoreLayout: true);
        _img.rectTransform.SetAsLastSibling();
        return true;
    }
}

/// 消息区里不显示邀请信本身（1.1 里邀请不是一条消息）：把那条消息的格子压成 0 高。
/// 09-25 SORA「特殊频段里有机率看不到商人的消息」：消息格子是 LightScroller 循环复用的，压扁 / 藏掉的格子会被拿去显示普通消息，
/// 不恢复的话还是 0 高、子物体还关着，那条消息就不见了。所以记下藏之前的高度，格子下一次显示前（前缀，原生 Show 之前）放回去
[HarmonyPatch(typeof(MessageFork), nameof(MessageFork.Show))]
public static class ChatInviteBubble
{
    class Hidden : MonoBehaviour
    {
        public float Height;
        public bool Active;
    }

    /// 09-25（SORA 14 / 15.png：一格里两种消息视图并排，后一个被挤到右边）：只还原高度，视图开关交给原生——
    /// 原生复用格子时先 Close（四个视图全关）再 Show（只开要用的那个），这里要是把藏之前记下的各视图开关原样放回，就在 Close 之后又把上次邀请信用过的视图打开了，
    /// 一格里就有两个视图、横排布局组把后一个挤到右边。Postfix 里再保证只开这条消息那一种
    static void Prefix(MessageFork __instance)
    {
        try
        {
            var h = __instance.GetComponent<Hidden>();
            if (h == null || !h.Active) return;
            h.Active = false;
            var le = __instance.GetComponent<LayoutElement>();
            if (le != null) le.enabled = false;
            var rt = (RectTransform)__instance.transform;
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, h.Height);
            LayoutRebuilder.MarkLayoutForRebuild(rt);
        }
        catch (Exception e) { Plugin.Log.LogWarning("[invite] could not restore a recycled message cell: " + e.Message); }
    }

    static void Postfix(MessageFork __instance, MessageData data)
    {
        OnlyOwnView(__instance, data);
        try
        {
            if (!InviteState.IsInviteMessage(data?.Message))
            {
                if (data != null && ChatInviteTabs.IsSpecialType(data.DialogueType)) ChatMessageStyle.Apply(__instance, data);
                return;
            }
            var h = __instance.GetComponent<Hidden>() ?? __instance.gameObject.AddComponent<Hidden>();
            var rt = (RectTransform)__instance.transform;
            foreach (Transform c in __instance.transform) c.gameObject.SetActive(false);
            h.Height = rt.sizeDelta.y;
            h.Active = true;
            var le = __instance.GetComponent<LayoutElement>() ?? __instance.gameObject.AddComponent<LayoutElement>();
            le.enabled = true; le.ignoreLayout = false; le.minHeight = 0f; le.preferredHeight = 0f; le.flexibleHeight = 0f;
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, 0f);
            LayoutRebuilder.MarkLayoutForRebuild(rt);
        }
        catch (Exception e) { Plugin.WarnOnce("invite/letter-bubble", "[invite] Failed to hide the invite letter bubble: " + e.Message); }
    }

    /// <summary>这条消息在格子里用的是哪个视图（和原生 MessageFork.Show 的分支一致）：自己的 / 对方的 / 系统提示（带参数的系统消息、且不在 SYSTEM 对话里）/ 附件消息（商人、SYSTEM 对话、不带参数的系统消息）。
    /// OnlyOwnView 和 ChatMessageStyle.Apply 共用这一份</summary>
    internal static MessageView ViewFor(MessageFork fork, MessageData data) => data.MessageType switch
    {
        EMessageViewType.YourMessage => fork._yourChatMessage,
        EMessageViewType.OpponentMessage => fork._opponentChatMessage,
        EMessageViewType.SystemMessage => data.DialogueType != EMessageType.SystemMessage && data.Message?.Params != null ? fork._systemMessage : fork._attachmentMessage,
        EMessageViewType.TraderMessage => fork._attachmentMessage,
        _ => null,
    };

    /// 一格只开这条消息那一种视图（原生 Show 只负责打开要用的那个，不关别的）
    static void OnlyOwnView(MessageFork fork, MessageData data)
    {
        if (fork == null || data == null) return;
        try
        {
            var own = ViewFor(fork, data);
            if (own == null) return;
            foreach (MessageView v in new MessageView[] { fork._yourChatMessage, fork._opponentChatMessage, fork._systemMessage, fork._attachmentMessage })
                if (v != null && v != own && v.gameObject.activeSelf) v.gameObject.SetActive(false);
        }
        catch (Exception e) { Plugin.WarnOnce("invite/cell-cleanup", "[invite] Message cell view cleanup failed: " + e.Message); }
    }
}

/// <summary>09-25 SORA 给的 1.1 截图（3.png，2560×1440，1 单位 = 1.333 像素）：特殊通讯里商人的文字消息没有头像、没有名字行，
/// 气泡是纯色 #191B1B（原生是带缺角的 message_corner_background）、宽度随内容收缩、最宽 669 单位，左沿离右栏左框 15 单位；
/// 正文 #A0AAB4 常规字重（原生 #C3CDD3 粗体），左边距 15、上下约 11 / 12；时间 #585D60 在气泡右下，离右边 16.5、离底 12；
/// 换日处有一条日期分隔线：1.5 单位高的 #202123 细线横跨 720 单位，正中留空写 dd/MM/yyyy（#595E61），上下各离气泡约 21 单位。
/// 原生格子的层级（09-25 日志里的结构记录）：MessageFork → 各 MessageView（VerticalLayoutGroup pad 20/150）→ Inner（图 + VerticalLayoutGroup pad 20/60/5/10
/// + 宽度 Unconstrained 的 ContentSizeFitter）→ SenderNickname / Message / Timestamp（忽略布局，右上）/ Corner / AccountType（忽略布局，头像）。
/// 格子是按对话各开一个容器、容器里循环复用的，特殊通讯的格子只会显示特殊通讯的消息，所以只改不还原。</summary>
public static class ChatMessageStyle
{
    const float BubbleLeft = 10f;   // 格子本身在容器里 x=5，右栏左框到容器 0.5：10 + 5 + ~0.5 ≈ 15
    const float BubbleMax = 669f;
    const float SeparatorWidth = 720f;
    static readonly Color BubbleColor = new Color32(0x19, 0x1B, 0x1B, 0xFF);
    static readonly Color TextColor = new Color32(0xA0, 0xAA, 0xB4, 0xFF);
    static readonly Color TimeColor = new Color32(0x59, 0x5E, 0x61, 0xFF);   // 1.1MCP 导出的原值
    static readonly Color LineColor = new Color32(0x20, 0x21, 0x23, 0xFF);
    static readonly Color DateColor = new Color32(0x59, 0x5E, 0x61, 0xFF);

    // 09-25 按 1.1MCP 导出的 SYSTEM 对话（captures\ui-ChatPart-0925-231715.txt）核了一遍气泡的排版，1.1 的附件消息：
    //   气泡内边距 左 15 / 右 15 / 上 12 / 下 12，正文和时间同一行（间隔 5，时间最少占 35、右对齐），正文到附件条 10；
    //   带附件时气泡宽 = 附件条 620 + 30 = 650，正文长了才撑宽，最宽 = 行宽 720 - 右留 50 = 670（我们 669）。
    // 0.16 的时间不进排版（我们钉在右下），所以正文右边留 15 + 5 + 35 = 55，正文能占的宽度就和 1.1 一样
    const int PadLeft = 15, PadRight = 55, PadTop = 12, PadBottom = 12;
    const float AttachBubble = 650f;

    public static void Apply(MessageFork fork, MessageData data)
    {
        try
        {
            // 只动对方 / 商人 / 附件视图：自己发的消息不改；SYSTEM 对话（09-25 SORA 29.png：还是原生的名字行、头像、缺角气泡）原生也用附件消息视图，
            // 1.1 里和商人消息同一套样式；带参数的系统提示走的是另一个视图（_systemMessage），1.1 那种没导出过，不动
            var view = ChatInviteBubble.ViewFor(fork, data);
            if (view == null || view == fork._yourChatMessage || view == fork._systemMessage) return;
            var inner = view.transform.Find("Inner") as RectTransform;
            if (inner == null) return;
            if (view.GetComponent<VerticalLayoutGroup>() is VerticalLayoutGroup vg && vg.padding.left != (int)BubbleLeft)
                vg.padding = new RectOffset((int)BubbleLeft, (int)(729f - BubbleLeft - BubbleMax), vg.padding.top, vg.padding.bottom);
            if (inner.GetComponent<Image>() is Image img) { img.sprite = null; img.type = Image.Type.Simple; img.color = BubbleColor; }
            if (inner.GetComponent<VerticalLayoutGroup>() is VerticalLayoutGroup ig
                && (ig.padding.left != PadLeft || ig.padding.right != PadRight || ig.padding.top != PadTop || ig.padding.bottom != PadBottom))
                ig.padding = new RectOffset(PadLeft, PadRight, PadTop, PadBottom);
            var attachView = view as AttachmentMessageView;
            var attach = attachView != null && attachView._messageAttachmentsObject != null ? attachView._messageAttachmentsObject.transform : inner.Find("AttachmentBlock");
            var hasAttach = attach != null && attach.gameObject.activeSelf;
            if (inner.GetComponent<LayoutElement>() is LayoutElement le) { le.enabled = true; le.minWidth = hasAttach ? AttachBubble : 0f; }
            foreach (var n in new[] { "SenderNickname", "AccountType", "Corner" })
                if (inner.Find(n) is Transform t && t.gameObject.activeSelf) t.gameObject.SetActive(false);
            if (view._senderMessage != null && view._senderMessage.GetComponent<TMP_Text>() is TMP_Text msg) { msg.color = TextColor; msg.fontStyle = FontStyles.Normal; }
            if (view._timestampLabel != null)
            {
                var ts = view._timestampLabel;
                ts.color = TimeColor;
                var trt = ts.rectTransform;
                trt.anchorMin = trt.anchorMax = new Vector2(1f, 0f); trt.pivot = new Vector2(1f, 0f);
                // 1.1：时间框右沿 = 气泡右沿 - 15，框底和正文末行框底齐平（带附件时 = 条顶 42 + 10）
                trt.anchoredPosition = new Vector2(-15f, hasAttach ? BarBottom + BarHeight + 10f : PadBottom);
            }
            if (hasAttach && attachView != null) AttachmentRow(attachView, (RectTransform)attach);
            Separator(view, data);
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)fork.transform);
        }
        catch (Exception e) { Plugin.Log.LogWarning("[invite] 1.1 message style failed: " + e.Message); }
    }

    // 1.1 的附件条（1.1MCP 导出 + 0.16 预制体 sharedassets44 对照，09-25）：
    //   1.1 = 贴气泡左下（左 15、底 12）的 465×30 底色条（系统 / 包裹黑、任务 #B6B4A6@0.55、保险 / BTR 各自的色——两边一样，原生按消息类型上色，不动），
    //   条里：图标固定 34×30（保持比例）左留 5，间隔 5 接文字（18 粗），倒计时 14 粗白、右对齐离条右沿 10、不带括号；
    //   条右边一个 150 宽的格子，「收取」按钮 120×30 或「(已收取)」20 号 #B2B1A4@0.3，在格子里居中 → 中心离条右沿 80。
    //   0.16 = 条是按气泡宽拉伸的一整块、底色四边各大 5、图标按原图尺寸（系统图标 41×36）、倒计时紧跟在文字后面带括号、按钮和「(已收取)」14 号白字贴在条右端里面。
    // 按钮本身两边一样（同一种 DefaultUIButton：Bender Shadowed 20、#E7E5D4→悬停黑、底图 alpha 0），只挪位置
    static readonly Color ReceivedColor = new Color(0xB2 / 255f, 0xB1 / 255f, 0xA4 / 255f, 0.3f);
    const float BarLeft = 15f, BarBottom = 12f, BarWidth = 465f, BarHeight = 30f, StatusCenter = 80f;

    static void AttachmentRow(AttachmentMessageView view, RectTransform block)
    {
        block.anchorMin = block.anchorMax = Vector2.zero;
        block.pivot = Vector2.zero;
        block.anchoredPosition = new Vector2(BarLeft, BarBottom);
        block.sizeDelta = new Vector2(BarWidth, BarHeight);
        if (view._messageBackground != null) view._messageBackground.rectTransform.Stretch();
        // 任务类消息的图标：两边引用的贴图同名（tab_icon_tasks_selected），1.1 换了画——28×28、没有 0.16 那圈浅色光晕（09-26 SORA 32.png 对 1.png；
        // 图是 ZoneExtract sprite 从 1.1 sharedassets44 解出来的）。换掉视图上的引用，原生按消息类型上图时就用 1.1 的；这一次 Show 已经上过旧图，顺手换掉
        var quest11 = VisitArt.Load("chat11_quest_icon.png");
        if (quest11 != null && view._questIcon != quest11)
        {
            var old = view._questIcon;
            view._questIcon = quest11;
            if (old != null && view._messageIcon != null && view._messageIcon.sprite == old) view._messageIcon.sprite = quest11;
        }
        if (view._messageIcon != null)
        {
            // 原生每次 Show 都 SetNativeSize，这里在它之后改回 1.1 的 34×30
            view._messageIcon.preserveAspect = true;
            var irt = view._messageIcon.rectTransform;
            irt.anchorMin = irt.anchorMax = new Vector2(0f, 0.5f); irt.pivot = new Vector2(0f, 0.5f);
            irt.anchoredPosition = new Vector2(5f, 0f);
            irt.sizeDelta = new Vector2(34f, 30f);
        }
        // 文字是条上的横排布局组排的：左内边距 5 + 34 + 5
        if (block.GetComponent<HorizontalLayoutGroup>() is HorizontalLayoutGroup hl && hl.padding.left != 44)
            hl.padding = new RectOffset(44, hl.padding.right, hl.padding.top, hl.padding.bottom);
        // 给附件条占位的 Limiter：条顶（12 + 30）到正文框底 10，减掉气泡布局的间隔 6
        if (view._insuranceLimiter != null && view._insuranceLimiter.GetComponent<LayoutElement>() is LayoutElement lle && lle.minHeight != BarHeight + 10f - 6f)
            lle.minHeight = BarHeight + 10f - 6f;
        if (view._timeToGetLabel != null)
        {
            var tl = view._timeToGetLabel;
            var tle = tl.GetComponent<LayoutElement>() ?? tl.gameObject.AddComponent<LayoutElement>();
            tle.ignoreLayout = true;
            var trt = tl.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(1f, 0.5f); trt.pivot = new Vector2(1f, 0.5f);
            trt.anchoredPosition = new Vector2(-10f, 0f);
            trt.sizeDelta = new Vector2(130f, BarHeight);
            tl.alignment = TextAlignmentOptions.Right;
            TimerText(tl);
        }
        foreach (var (rt, width) in new (RectTransform, float)[] {
            (view._receivedLabel != null ? view._receivedLabel.transform as RectTransform : null, 100f),
            (view._outOfTimeLabel != null ? view._outOfTimeLabel.transform as RectTransform : null, 100f),
            (view._transferButtonSpawner != null ? view._transferButtonSpawner.transform as RectTransform : null, 120f) })
        {
            if (rt == null) continue;
            rt.anchoredPosition = new Vector2(StatusCenter + width / 2f, 0f);
        }
        if (view._receivedLabel != null && view._receivedLabel.GetComponent<TMP_Text>() is TMP_Text received)
        {
            received.color = ReceivedColor;
            received.fontSize = 20f;
        }
        // 「(已过期)」：1.1 预制体里是 #9C9A8F@0.3、框高 22.01（= 20 号字，和「(已收取)」一样），09-26 从 1.1 sharedassets44 读的
        if (view._outOfTimeLabel != null && view._outOfTimeLabel.GetComponent<TMP_Text>() is TMP_Text expired)
        {
            expired.color = ExpiredColor;
            expired.fontSize = 20f;
        }
    }

    static readonly Color ExpiredColor = new Color(0x9C / 255f, 0x9A / 255f, 0x8F / 255f, 0.3f);

    /// 倒计时去掉括号：0.16 写「(01:23:59:28)」，1.1 是「01:23:59:28」
    static void TimerText(TMP_Text label)
    {
        var s = label.text;
        if (s != null && s.Length > 2 && s[0] == '(' && s[s.Length - 1] == ')') label.text = s.Substring(1, s.Length - 2);
    }

    /// 原生每秒在 Update 里重写一次倒计时
    [HarmonyPatch(typeof(AttachmentMessageView), nameof(AttachmentMessageView.Update))]
    public static class TimerFormat
    {
        static void Postfix(AttachmentMessageView __instance)
        {
            if (__instance._timeToGetLabel != null && __instance._timeToGetLabel.gameObject.activeSelf) TimerText(__instance._timeToGetLabel);
        }
    }

    /// 换日分隔线：这条消息和上一条（邀请信不算）不是同一天才亮
    /// 09-25：分隔线完全不进布局（放进消息视图的布局里的话，带分隔线的那一格会被撑宽挤到左框，或者整格右移 125 单位）——
    /// 要分隔线时把这一格的上边距加高 42，分隔线忽略布局、钉在空出来的那块上
    const int SeparatorPad = 42;

    static void Separator(MessageView view, MessageData data)
    {
        var sep = view.transform.Find("VisitDateSeparator");
        var date = data.Message.LocalDateTime.Date;
        var show = !SameDayAsPrevious(data, date);
        if (view.GetComponent<VerticalLayoutGroup>() is VerticalLayoutGroup vg && vg.padding.top != (show ? SeparatorPad : 0))
            vg.padding = new RectOffset(vg.padding.left, vg.padding.right, show ? SeparatorPad : 0, vg.padding.bottom);
        if (!show) { if (sep != null) sep.gameObject.SetActive(false); return; }
        if (sep == null) sep = BuildSeparator(view);
        sep.gameObject.SetActive(true);
        var label = sep.Find("Date/Text").GetComponent<TMP_Text>();
        label.text = date.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);
    }

    static bool SameDayAsPrevious(MessageData data, DateTime date)
    {
        var messages = data.SocialNetwork?.SelectedDialogue?.ChatMessages;
        if (messages == null) return false;
        DialogueChatMessage prev = null;
        foreach (var m in messages)
        {
            if (ReferenceEquals(m, data.Message)) break;
            if (!InviteState.IsInviteMessage(m)) prev = m;
        }
        return prev != null && prev.LocalDateTime.Date == date;
    }

    static Transform BuildSeparator(MessageView view)
    {
        var font = view._timestampLabel != null ? view._timestampLabel.font : null;
        // 不进布局：钉在消息视图顶部、从气泡左沿开始（上边距让出的 42 单位就是它的位置），线和日期按 720 画
        var rt = Chat11.Rect(view.transform, "VisitDateSeparator", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(BubbleLeft, 0f), new Vector2(SeparatorWidth, SeparatorPad), ignoreLayout: true);
        Chat11.Img(rt, "Line", null, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(SeparatorWidth, 1.5f), preserveAspect: false, color: LineColor);
        // 日期底下垫一块黑，把线在正中断开（1.1 断口 110 像素 = 82.5 单位）
        var brt = Chat11.Img(rt, "Date", null, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(SeparatorWidth / 2f, 0f), new Vector2(82.5f, 14f), preserveAspect: false, color: Color.black).rectTransform;
        var trt = Chat11.Span(brt, "Text", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var t = trt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = 12f; t.color = DateColor; t.alignment = TextAlignmentOptions.Center; t.enableWordWrapping = false; t.raycastTarget = false;
        return rt;
    }
}
