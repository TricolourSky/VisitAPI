using System;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using EFT.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VisitAPI.Native;

/// <summary>0.16 这边的界面结构导出，格式和 1.1MCP 的 V11.UI 一样（节点、组件、RectTransform、Image、TMP、布局、按钮动画），
/// 方便两边逐项对比。写到 BepInEx\VisitAPI-uidump\*.txt。控制台 visit_uidump &lt;物体名&gt; 或代码里直接调。</summary>
public static class UiDump
{
    public static string Dir => Path.Combine(BepInEx.Paths.BepInExRootPath, "VisitAPI-uidump");   // 写全名：Assembly-CSharp 里也有一个 Paths

    public static string Write(Transform root, string tag, int depth = 25, bool activeOnly = false)
    {
        if (root == null) return null;
        try
        {
            var sb = new StringBuilder();
            var canvas = root.GetComponentInParent<Canvas>();
            sb.AppendLine($"# {PathOf(root)}  captured {DateTime.Now:yyyy-MM-dd HH:mm:ss}  screen {Screen.width}x{Screen.height}");
            if (canvas != null) sb.AppendLine($"# root canvas '{canvas.rootCanvas.name}' scaleFactor={canvas.rootCanvas.scaleFactor:0.####}");
            _activeOnly = activeOnly;
            Node(root, 0, depth, sb);
            Directory.CreateDirectory(Dir);
            var safe = new string(tag.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
            var file = Path.Combine(Dir, $"ui016-{safe}-{DateTime.Now:MMdd-HHmmss}.txt");
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
            return file;
        }
        catch (Exception e) { Plugin.Log.LogWarning("[uidump] failed: " + e.Message); return null; }
    }

    public static string PathOf(Transform t)
    {
        var s = t.name;
        for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
        return s;
    }

    static bool _activeOnly;

    static void Node(Transform t, int level, int depth, StringBuilder sb)
    {
        if (_activeOnly && !t.gameObject.activeInHierarchy) return;
        var pad = new string(' ', level * 2);
        var go = t.gameObject;
        var comps = go.GetComponents<Component>().Where(c => c != null && !(c is RectTransform) && !(c is CanvasRenderer)).Select(c => c.GetType().Name).ToList();
        sb.Append(pad).Append(go.name).Append(go.activeSelf ? "" : " [off]");
        if (comps.Count > 0) sb.Append("  {").Append(string.Join(", ", comps)).Append('}');
        sb.AppendLine();
        if (t is RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            sb.Append(pad).Append($"  rect a({rt.anchorMin.x:0.##},{rt.anchorMin.y:0.##})-({rt.anchorMax.x:0.##},{rt.anchorMax.y:0.##}) pivot({rt.pivot.x:0.##},{rt.pivot.y:0.##}) pos({rt.anchoredPosition.x:0.##},{rt.anchoredPosition.y:0.##}) size({rt.sizeDelta.x:0.##},{rt.sizeDelta.y:0.##}) actual({rt.rect.width:0.##},{rt.rect.height:0.##}) world({c[0].x:0.#},{c[0].y:0.#})-({c[2].x:0.#},{c[2].y:0.#})").AppendLine();
        }
        var img = go.GetComponent<Image>();
        if (img != null)
            sb.Append(pad).Append($"  image sprite={(img.sprite == null ? "null" : img.sprite.name)} color={Hex(img.color)} type={img.type}{(img.enabled ? "" : " [disabled]")}{(img.preserveAspect ? " preserveAspect" : "")}").AppendLine();
        var txt = go.GetComponent<TMP_Text>();
        if (txt != null)
        {
            var s = (txt.text ?? "").Replace("\n", "\\n");
            if (s.Length > 120) s = s.Substring(0, 120) + "…";
            sb.Append(pad).Append($"  text \"{s}\" font={(txt.font == null ? "null" : txt.font.name)} size={txt.fontSize:0.##} color={Hex(txt.color)} align={txt.alignment} style={txt.fontStyle}{(txt.enabled ? "" : " [disabled]")}").AppendLine();
        }
        var le = go.GetComponent<LayoutElement>();
        if (le != null) sb.Append(pad).Append($"  layout min({le.minWidth:0.#},{le.minHeight:0.#}) pref({le.preferredWidth:0.#},{le.preferredHeight:0.#}) flex({le.flexibleWidth:0.#},{le.flexibleHeight:0.#}){(le.ignoreLayout ? " ignore" : "")}").AppendLine();
        var hlg = go.GetComponent<HorizontalOrVerticalLayoutGroup>();
        if (hlg != null) sb.Append(pad).Append($"  group {hlg.GetType().Name} pad({hlg.padding.left},{hlg.padding.right},{hlg.padding.top},{hlg.padding.bottom}) spacing={hlg.spacing:0.#} align={hlg.childAlignment}").AppendLine();
        var btn = go.GetComponent<DefaultUIButton>();
        if (btn != null)
            sb.Append(pad).Append($"  defaultButton minWidth={btn._minWidth:0.#} fontSize={btn._fontSize} iconContainer={(btn._iconContainer != null ? btn._iconContainer.name : "null")} icon={(btn._iconImage != null ? btn._iconImage.name : "null")} iconIdle={(btn._iconIdleImage != null ? btn._iconIdleImage.name : "null")} header={(btn._headerLabel != null ? btn._headerLabel.name : "null")} size={(btn._sizeLabel != null ? btn._sizeLabel.name : "null")}").AppendLine();
        var anim = go.GetComponent<DefaultUIButtonAnimation>();
        if (anim != null)
            sb.Append(pad).Append($"  buttonAnim bgNormalAlpha={anim._backgorundNormalStateAplha:0.##} label {Hex(anim._normalLabelColor)}->{Hex(anim._highlightedLabelColor)} attachments={anim._stateAttachments?.Count ?? 0} bg={(anim.Background != null ? anim.Background.name : "null")} image={(anim.Image != null ? anim.Image.name : "null")} icon={(anim.Icon != null ? anim.Icon.name : "null")}").AppendLine();
        if (level >= depth) return;
        for (var i = 0; i < t.childCount; i++) Node(t.GetChild(i), level + 1, depth, sb);
    }

    static string Hex(Color c) => $"#{(int)Math.Round(c.r * 255):X2}{(int)Math.Round(c.g * 255):X2}{(int)Math.Round(c.b * 255):X2}{(c.a < 0.999f ? $"@{c.a:0.##}" : "")}";

    /// 按名字找场景里的物体（包括 DontDestroyOnLoad 和未激活的），取第一个
    public static Transform Find(string name) =>
        Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t != null && t.gameObject.scene.IsValid() && string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase));
}
