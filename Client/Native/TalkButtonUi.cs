using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace VisitAPI.Native;

public static class TalkButtonUi
{
    public static GameObject Build(EFT.UI.TraderScreensGroup screen, UnityAction onClick)
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
        label.fontSize = 16f; label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white; label.raycastTarget = false;
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
