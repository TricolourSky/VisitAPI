using EFT.Dialogs;
using EFT.UI;
using UnityEngine;

namespace VisitAPI.Native;

// 旁白改走原生字幕框：TraderDialogScreen._subtitlesView 就是正式版那条底部字幕
// （4.0.13 当年也是驱动它，只不过那会儿字段是私有的、得反射）。旁白拍上把商人对话窗藏掉、
// 把文字写进字幕条，点一下 / 按空格推进 —— 推进走的还是那条被藏起来的"继续…"玩家行，
// 所以对话模板一个字节都没改，背景/BGM/语音/once/任务门全部照旧。
public class NarrationView : MonoBehaviour
{
    ClientDialogController _dc;
    TraderDialogScreen _screen;
    BaseTraderDialog _beat;   // 非空 = 正停在旁白拍上
    float _armAt;

    public static void Attach(ClientDialogController dc, TraderDialogScreen screen)
    {
        var view = screen.GetComponent<NarrationView>() ?? screen.gameObject.AddComponent<NarrationView>();
        if (view._dc != null) view._dc.OnDialogChanged -= view.OnDialog;
        view._dc = dc; view._screen = screen;
        dc.OnDialogChanged += view.OnDialog;
        view.OnDialog(dc.CurrentDialog);
    }

    void OnDialog(BaseTraderDialog dialog)
    {
        if (dialog == null || !DialogTemplateBuilder.NarrationByDialog.TryGetValue(dialog.Id, out var text)) { Restore(); return; }
        _beat = dialog;
        _armAt = Time.unscaledTime + 0.25f;   // 开屏 / 上一拍那一下点击别把这拍也吃掉
        _screen._subtitlesView._textField.text = text;
        _screen._subtitlesView.ShowGameObject();
        _screen._dialogWindow.HideGameObject();
    }

    // 每帧按回去，而且放 LateUpdate：窗口那边是 EventDialogWindow.Redraw -> RedrawAsync 异步显示的
    // （它重写了 Redraw 且不调 base，所以挂补丁没用），只能靠兜底。LateUpdate 排在 Update 和
    // 异步续体后面，绝大多数情况下同帧就按回去了，看不到闪。
    void LateUpdate()
    {
        if (_beat == null || _dc == null || _dc.CurrentDialog != _beat) return;
        _screen._dialogWindow.HideGameObject();
        _screen._subtitlesView.ShowGameObject();
        // NPC 那一拍是引擎自动过的，点击只认玩家拍（就是被藏起来的"继续…"行）
        if (_beat.DialogSide != EDialogSide.Player || Time.unscaledTime < _armAt) return;
        if (!Input.GetMouseButtonDown(0) && !Input.GetKeyDown(KeyCode.Space)) return;
        _armAt = Time.unscaledTime + 0.25f;
        _dc.ExecuteLineByIndex(0);
    }

    // 对话屏是池化的（关闭只是隐藏），窗口不自己恢复的话下次开原版商人对话就是一片空白
    void Restore()
    {
        if (_beat == null) return;
        _beat = null;
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
