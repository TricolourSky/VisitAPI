using System;
using System.Collections.Generic;
using System.Linq;
using EFT.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VisitAPI.Native;

/// 09-26：标记我们克隆出来的原生按钮（聊天窗「访问 / 回复」）。关键抉择窗要找「原生的」DefaultUIButton 当模板，
/// 以前随手取场景里第一个，结果取到了带电话图标的回复按钮（SORA：「按钮出问题了」），靠这个标记跳过
public class VisitClonedButton : MonoBehaviour { }

/// <summary>09-25 起聊天窗照 1.1 原样还原的共用件。素材是用 1.1MCP 从运行中的 1.1 客户端导出的原图（Client\art\chat11_*.png，
/// 对照记录见 1.1MCP\captures\notes-0925.md / ui-ChatScreen-*.txt），尺寸、颜色、字体都取自 1.1 的界面结构导出，不再照截图量。</summary>
public static class Chat11
{
    public static readonly Color Border = new Color32(0x58, 0x5D, 0x60, 0xFF);
    public static readonly Color ButtonText = new Color32(0xE7, 0xE5, 0xD4, 0xFF);

    /// border_generic：16×16、九宫格边 2（1.1 里是 Sliced、pixelsPerUnitMultiplier 1）
    public static Sprite BorderSprite => VisitArt.Load("chat11_border.png", new Vector4(2f, 2f, 2f, 2f));
    public static Sprite ButtonIdle => VisitArt.Load("chat11_button_idle.png");

    static readonly Dictionary<string, TMP_FontAsset> _fonts = new();

    /// 按名字找游戏里已加载的 TMP 字体（1.1 用的是 "Jovanny Lemonad - Bender Normal SDF" / "... Bender Shadowed SDF"），找不到就用 fallback
    public static TMP_FontAsset Font(string contains, TMP_FontAsset fallback)
    {
        if (_fonts.TryGetValue(contains, out var hit) && hit != null) return hit;
        TMP_FontAsset found = null;
        try { found = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(f => f != null && f.name.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0); }
        catch { }
        if (found != null) _fonts[contains] = found;
        return found ?? fallback;
    }

    /// 一个铺满父物体的 Image（可带偏移），用来挂底图 / 边框
    public static Image Fill(RectTransform parent, string name, Sprite sprite, Color color, Vector2 offMin, Vector2 offMax, Image.Type type = Image.Type.Simple)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.GetComponent<LayoutElement>().ignoreLayout = true;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = offMin; rt.offsetMax = offMax;
        var img = go.GetComponent<Image>();
        img.sprite = sprite; img.type = type; img.color = color; img.raycastTarget = false;
        return img;
    }

    /// 1.1 的边框：border_generic 九宫格 #585D60，比本体向右上各多 1 单位（1.1 里 pos(0.5,0.5) sizeDelta(1,1)）
    public static Image Frame(RectTransform parent) => Fill(parent, "Border", BorderSprite, Border, Vector2.zero, new Vector2(1f, 1f), Image.Type.Sliced);

    /// <summary>克隆 0.16 原生的 DefaultUIButton（聊天窗「收取全部」那个），按 1.1 聊天标题栏 ReplyByRadio / VisitAtLobby 的参数改：
    /// 内边距 20/20/8/8、最小高 30、平时背景透明、背景图 button_idle、文字 #E7E5D4 → 悬停黑、图标平时 / 悬停两张。
    /// 0.16 和 1.1 的 DefaultUIButtonAnimation 是同一个类（悬停时背景从 -15 滑入并淡入），所以动画是原生的。
    /// 克隆失败（没有原生按钮可抄）返回 null，调用方自己兜底。</summary>
    public static DefaultUIButton CloneButton(DefaultUIButton src, RectTransform parent, string name, string text, float fontSize, Sprite iconIdle, Sprite iconHover)
    {
        if (src == null) return null;
        GameObject go = null;
        try
        {
            go = UnityEngine.Object.Instantiate(src.gameObject, parent, false);
            go.name = name;
            go.AddComponent<VisitClonedButton>();
            var btn = go.GetComponent<DefaultUIButton>();
            var anim = go.GetComponent<DefaultUIButtonAnimation>();
            if (btn == null) { UnityEngine.Object.Destroy(go); return null; }
            if (anim != null)
            {
                if (anim._stateAttachments != null)
                {
                    foreach (var kv in anim._stateAttachments) if (kv.Value != null) UnityEngine.Object.Destroy(kv.Value);
                    anim._stateAttachments.Clear();
                }
                anim._changeColors = false;
                anim._backgorundNormalStateAplha = 0f;
                anim._normalLabelColor = ButtonText;
                anim._highlightedLabelColor = Color.black;
                if (anim.Image != null) { anim.Image.sprite = ButtonIdle; anim.Image.type = Image.Type.Simple; }
            }
            // 09-25 实机：克隆出来比 1.1 宽一截（悬停块约 200 像素，1.1 是 144）——0.16「收取全部」原件带着最小宽度，这里清掉，宽度只由内边距 + 内容决定
            btn._minWidth = -1f;
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.ignoreLayout = true; le.minHeight = 30f; le.minWidth = -1f; le.preferredWidth = -1f; le.flexibleWidth = -1f;
            var hlg = go.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null) { hlg.padding = new RectOffset(20, 20, 8, 8); hlg.childAlignment = TextAnchor.MiddleCenter; }
            var fit = go.GetComponent<ContentSizeFitter>() ?? go.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            btn.OnClick.RemoveAllListeners();
            go.SetActive(true);
            btn.SetIcon(iconHover, iconIdle);   // DefaultUIButton：_iconImage = 悬停时淡入的那张，_iconIdleImage = 平时那张
            // 09-25 两边结构导出对比（BepInEx\VisitAPI-uidump vs 1.1MCP\captures\ui-ChatScreen-*）：
            // 0.16「收取全部」平时不显示图标，IconIdle 的 alpha 是 0——1.1 回复按钮平时显示电话，alpha 1；
            // 1.1 的图标框比文字行高 8（23.4，0.16 原件 15.4）、往左挪 4，文字往右挪 2.5
            if (btn._iconIdleImage != null) btn._iconIdleImage.color = Color.white;
            if (btn._iconContainer != null && btn._iconContainer.transform is RectTransform ic)
            {
                ic.anchoredPosition = new Vector2(-4f, ic.anchoredPosition.y);
                ic.sizeDelta = new Vector2(ic.sizeDelta.x, 8f);
            }
            if (btn._headerLabel != null && btn._headerLabel.transform is RectTransform lab) lab.anchoredPosition = new Vector2(2.5f, lab.anchoredPosition.y);
            btn.SetRawText(text, (int)fontSize);
            btn.Interactable = true;
            anim?.SetState(EButtonAnimationState.Normal);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)go.transform);
            return btn;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[invite] cloning the native button for '{name}' failed, using a plain one: {e.Message}");
            if (go != null) UnityEngine.Object.Destroy(go);
            return null;
        }
    }
}

/// 1.1「收取全部」的三态：平时 / 悬停 / 按下各一张底图（Reciewe-All_Chat-Button_0/1/2），悬停和按下时文字变黑、回形针换黑色那张
public class Chat11States : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    public Image Target;
    public Sprite Normal, Hover, Pressed;
    public TMP_Text Label;
    public Color LabelNormal = Chat11.ButtonText, LabelHover = Color.black;
    public Image Icon;
    public Sprite IconNormal, IconHover;
    bool _in, _down;

    public void OnPointerEnter(PointerEventData e) { _in = true; Apply(); }
    public void OnPointerExit(PointerEventData e) { _in = false; _down = false; Apply(); }
    public void OnPointerDown(PointerEventData e) { _down = true; Apply(); }
    public void OnPointerUp(PointerEventData e) { _down = false; Apply(); }
    void OnDisable() { _in = _down = false; Apply(); }

    public void Apply()
    {
        var lit = _in || _down;
        if (Target != null) Target.sprite = _down && Pressed != null ? Pressed : lit && Hover != null ? Hover : Normal;
        if (Label != null) Label.color = lit ? LabelHover : LabelNormal;
        if (Icon != null && IconNormal != null) Icon.sprite = lit && IconHover != null ? IconHover : IconNormal;
    }
}

/// 页签的悬停：只记状态，由页签自己按「选中 / 悬停 / 平时」换底图
public class Chat11TabHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public bool Hovered;
    public Action Changed;
    public void OnPointerEnter(PointerEventData e) { Hovered = true; Changed?.Invoke(); }
    public void OnPointerExit(PointerEventData e) { Hovered = false; Changed?.Invoke(); }
    void OnDisable() { if (!Hovered) return; Hovered = false; Changed?.Invoke(); }
}
