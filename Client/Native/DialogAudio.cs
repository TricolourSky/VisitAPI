using EFT.Dialogs;
using UnityEngine;

namespace VisitAPI.Native;

public class DialogAudio : MonoBehaviour
{
    ClientDialogController _controller;
    AudioSource _voice, _bgm;
    string _bgmFile;

    public static void Attach(ClientDialogController controller, GameObject host)
    {
        var a = host.AddComponent<DialogAudio>();
        a._controller = controller;
        a._voice = host.AddComponent<AudioSource>(); a._voice.spatialBlend = 0f;
        a._bgm = host.AddComponent<AudioSource>(); a._bgm.spatialBlend = 0f; a._bgm.loop = true; a._bgm.volume = 0.5f;
        controller.OnDialogChanged += a.OnDialog;
        a.OnDialog(controller.CurrentDialog);
    }

    void OnDialog(BaseTraderDialog dialog)
    {
        if (dialog == null) { Destroy(this); return; }
        if (DialogTemplateBuilder.VoiceByDialog.TryGetValue(dialog.Id, out var voice))
            AudioFiles.Load(voice, clip => { if (_voice == null) return; _voice.Stop(); _voice.clip = clip; _voice.Play(); });
        if (DialogTemplateBuilder.BgmByDialog.TryGetValue(dialog.Id, out var bgm) && bgm != _bgmFile)
        {
            _bgmFile = bgm;
            AudioFiles.Load(bgm, clip => { if (_bgm == null) return; _bgm.Stop(); _bgm.clip = clip; _bgm.Play(); });
        }
    }

    void OnDestroy()
    {
        _controller.OnDialogChanged -= OnDialog;
        if (_voice != null) Destroy(_voice);
        if (_bgm != null) Destroy(_bgm);
        AudioFiles.ReleaseAll();
    }
}
