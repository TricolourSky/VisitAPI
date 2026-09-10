using System;
using System.Collections;
using System.IO;
using EFT.Dialogs;
using UnityEngine;
using UnityEngine.UI;

namespace VisitAPI.Native;

/// <summary>自定义背景图/视频的宿主，也是整个自定义会话展示层的生命周期锚点（它一销毁就 SceneLoader.Close()）。</summary>
public class DialogBackground : MonoBehaviour
{
    static DialogBackground _live;
    /// <summary>@trade/@tasks/@services 往返商人页期间保住背景不销毁（TabRouter 置 true，重挂完成置 false）。</summary>
    public static bool KeepAlive;
    ClientDialogController _controller;
    RawImage _image;
    string _file;   // 当前挂着的背景（文件里的原文，含 once/loop 尾缀）

    public static void Attach(ClientDialogController controller) => Plugin.Instance.StartCoroutine(Find(controller));

    public static void Discard()
    {
        KeepAlive = false;
        if (_live != null) { SceneLoader.Close(); Destroy(_live.gameObject); }
    }

    public static void Cover()
    {
        if (_live != null) _live.transform.SetAsLastSibling();
    }

    static IEnumerator Find(ClientDialogController controller)
    {
        yield return UiWait.Until(() => DialogScreenTracker.Open, 120);
        var screen = DialogScreenTracker.Live;
        if (screen == null) yield break;   // 屏一直没亮：和旧版一样静默放弃
        var bg = _live;
        if (bg == null)
        {
            var rt = new GameObject("VisitBgRoot", typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(screen.transform.parent, false);
            rt.SetSiblingIndex(0);
            bg = _live = rt.Stretch().gameObject.AddComponent<DialogBackground>();
        }
        else if (bg._controller != null) bg._controller.OnDialogChanged -= bg.OnDialog;
        bg.transform.SetSiblingIndex(0);
        KeepAlive = false;
        bg._controller = controller;
        controller.OnDialogChanged += bg.OnDialog;
        bg.OnDialog(controller.CurrentDialog);
        DialogAudio.Attach(controller, screen.gameObject);
        NarrationView.Attach(controller, screen);
    }

    void OnDialog(BaseTraderDialog dialog)
    {
        if (dialog == null) { if (!KeepAlive) { SceneLoader.Close(); Destroy(gameObject); } return; }
        if (SceneLoader.Requested || !DialogTemplateBuilder.BgByDialog.TryGetValue(dialog.Id, out var file)) return;
        // 同一张连着来（NPC 拍和「继续…」拍、没写自己 bg 的几拍都登记节点 bg）不重载：视频会从头再播、图会闪一下（09-10）
        if (file == _file) return;
        _file = file;
        // 背景文件名可带 " once"/" loop" 尾缀控制视频是否循环, 默认循环(.dlg 作者约定)。
        // JS 侧的对照实现在 VisitAPI Editor 的 index.html bgCut/bgOnce/bgVid —— 改这里必须同时改那里。
        var loop = !file.EndsWith(" once", StringComparison.Ordinal);
        if (!loop || file.EndsWith(" loop", StringComparison.Ordinal)) file = file.Substring(0, file.LastIndexOf(' ')).TrimEnd();
        var path = Path.Combine(DialogFiles.Loader.BaseDir, file.Contains("/") || file.Contains("\\") ? file : Path.Combine("backgrounds", file));
        if (!File.Exists(path)) { Plugin.Log.LogWarning("[bg] file not found: " + path); return; }
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".mp4" || ext == ".webm" || ext == ".m4v" || ext == ".mov") { DialogVideo.Play(Image(), path, loop); return; }
        DialogVideo.Stop(_image);
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(path));
        var old = Image().texture;
        _image.texture = tex;
        if (old is Texture2D) Destroy(old);
    }

    RawImage Image()
    {
        if (_image != null) return _image;
        var rt = new GameObject("VisitBg", typeof(RectTransform), typeof(RawImage)).GetComponent<RectTransform>();
        rt.SetParent(transform, false);
        rt.SetSiblingIndex(0);
        _image = rt.Stretch().GetComponent<RawImage>();
        _image.raycastTarget = false;
        return _image;
    }

    void OnDestroy()
    {
        if (_live == this) _live = null;
        if (_controller != null) _controller.OnDialogChanged -= OnDialog;
        DialogVideo.Stop(_image);
        if (_image != null)
        {
            if (_image.texture is Texture2D tex) { _image.texture = null; Destroy(tex); }
            Destroy(_image.gameObject);
        }
    }
}
