using System.Collections;
using System.IO;
using EFT.Dialogs;
using UnityEngine;
using UnityEngine.UI;

namespace VisitAPI.Native;

public class DialogBackground : MonoBehaviour
{
    static DialogBackground _live;
    public static bool KeepAlive;
    ClientDialogController _controller; RawImage _image;

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
        for (var i = 0; i < 120; i++)
        {
            var screen = FindObjectOfType<EFT.UI.TraderDialogScreen>();
            if (screen == null) { yield return null; continue; }
            var bg = _live;
            if (bg == null)
            {
                var rt = new GameObject("VisitBgRoot", typeof(RectTransform)).GetComponent<RectTransform>();
                rt.SetParent(screen.transform.parent, false); rt.SetSiblingIndex(0);
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
                bg = _live = rt.gameObject.AddComponent<DialogBackground>();
            }
            else if (bg._controller != null) bg._controller.OnDialogChanged -= bg.OnDialog;
            bg.transform.SetSiblingIndex(0);
            KeepAlive = false;
            bg._controller = controller; controller.OnDialogChanged += bg.OnDialog; bg.OnDialog(controller.CurrentDialog);
            DialogAudio.Attach(controller, screen.gameObject);
            yield break;
        }
    }

    void OnDialog(BaseTraderDialog dialog)
    {
        if (dialog == null) { if (!KeepAlive) { SceneLoader.Close(); Destroy(gameObject); } return; }
        if (SceneLoader.Requested || !DialogTemplateBuilder.BgByDialog.TryGetValue(dialog.Id, out var file)) return;
        // 背景文件名可带 " once"/" loop" 尾缀控制视频是否循环, 默认循环(.dlg 作者约定)
        var loop = !file.EndsWith(" once"); if (!loop || file.EndsWith(" loop")) file = file.Substring(0, file.LastIndexOf(' '));
        var path = Path.Combine(DialogFiles.Loader.BaseDir, file.Contains("/") || file.Contains("\\") ? file : Path.Combine("backgrounds", file));
        if (!File.Exists(path)) { Plugin.Log.LogWarning("[bg] file not found: " + path); return; }
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".mp4" || ext == ".webm" || ext == ".m4v" || ext == ".mov") { DialogVideo.Play(Image(), path, loop); return; }
        DialogVideo.Stop(_image);
        var tex = new Texture2D(2, 2); tex.LoadImage(File.ReadAllBytes(path));
        var old = Image().texture; _image.texture = tex; if (old is Texture2D) Destroy(old);
    }

    RawImage Image()
    {
        if (_image != null) return _image;
        var rt = new GameObject("VisitBg", typeof(RectTransform), typeof(RawImage)).GetComponent<RectTransform>();
        rt.SetParent(transform, false); rt.SetSiblingIndex(0);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        _image = rt.GetComponent<RawImage>(); _image.raycastTarget = false;
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
