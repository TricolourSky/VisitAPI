using EFT.Dialogs;
using EFT.UI;
using UnityEngine;

namespace VisitAPI.Native;

public class NarrationView : MonoBehaviour
{
    ClientDialogController _dc;
    TraderDialogScreen _screen;
    BaseTraderDialog _beat;
    string _text;
    float _armAt;

    public static void Attach(ClientDialogController dc, TraderDialogScreen screen)
    {
        var view = screen.GetComponent<NarrationView>() ?? screen.gameObject.AddComponent<NarrationView>();
        if (view._dc != null) view._dc.OnDialogChanged -= view.OnDialog;
        view._dc = dc;
        view._screen = screen;
        dc.OnDialogChanged += view.OnDialog;
        view.OnDialog(dc.CurrentDialog);
    }

    void OnDialog(BaseTraderDialog dialog)
    {
        if (dialog == null || !DialogTemplateBuilder.NarrationByDialog.TryGetValue(dialog.Id, out var text)) { Restore(); return; }
        _beat = dialog;
        _text = text;
        _armAt = Time.unscaledTime + 0.25f;
        _screen._subtitlesView._textField.text = text;
        _screen._subtitlesView.ShowGameObject();
        _screen._dialogWindow.HideGameObject();
    }

    void LateUpdate()
    {
        if (_beat == null || _dc == null || _dc.CurrentDialog != _beat) return;
        _screen._dialogWindow.HideGameObject();
        _screen._subtitlesView.ShowGameObject();
        if (_screen._subtitlesView._textField.text != _text) _screen._subtitlesView._textField.text = _text;
        if (_beat.DialogSide != EDialogSide.Player || Time.unscaledTime < _armAt) return;
        if (!Input.GetMouseButtonDown(0) && !Input.GetKeyDown(KeyCode.Space)) return;
        _armAt = Time.unscaledTime + 0.25f;
        _dc.ExecuteLineByIndex(0);
    }

    void Restore()
    {
        if (_beat == null) return;
        _beat = null;
        _text = null;
        if (_screen == null) return;
        _screen._subtitlesView._textField.text = string.Empty;
        _screen._subtitlesView.HideGameObject();
        _screen._dialogWindow.ShowGameObject();
    }

    void OnDestroy()
    {
        if (_dc != null) _dc.OnDialogChanged -= OnDialog;
        Restore();
    }
}
