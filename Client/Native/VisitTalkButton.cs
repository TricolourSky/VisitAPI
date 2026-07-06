using EFT.UI;
using UnityEngine;
using VisitAPI.Scene;

namespace VisitAPI.Native
{
    // Tags THE single visit entry button on the out-of-raid trade screen and carries the live
    // TraderScreensGroup + the currently-selected trader id. The entry patch re-points these on every trader
    // selection; the click reads them live. One button, two content paths: a trader WITH a `.dlg` opens the
    // VisitAPI dialog (which stages a 3D scene itself when the .dlg declares `scene:`); a retail vendor with
    // a scene bundle (and no .dlg) opens the native retail replay. A .dlg always wins over a bundle.
    internal sealed class VisitTalkButton : MonoBehaviour
    {
        internal object? Screen;
        internal string TraderId = "";
        internal DefaultUIButton? Button;
        private bool _hasDlg;
        private bool _hasScene;

        internal void Configure(object screen, string traderId, bool hasDlg, bool hasScene)
        {
            Screen = screen;
            TraderId = traderId ?? "";
            _hasDlg = hasDlg;
            _hasScene = hasScene;
            if (Button != null)
            {
                // .dlg trader: localized 对话. Retail replay: localized 拜访.
                string label = hasDlg ? Loc.DefaultTalkLabel : Loc.Pick("拜访", "Visit");
                Button.SetRawText(label, 24);
            }
        }

        internal void OnTalkClicked()
        {
            if (Screen == null || string.IsNullOrEmpty(TraderId)) return;
            NativeBinder.ActiveTradeScreen = Screen;

            if (!_hasDlg)
            {
                if (_hasScene) SceneStage.TryOpenRetail(TraderId);
                return;
            }

            DialogTree? tree = DialogTreeLoader.Load(TraderId);
            if (tree == null)
            {
                Plugin.Log.LogWarning("[TalkButton] no .dlg tree for " + TraderId);
                return;
            }

            object? profile = NativeBinder.GetTsgProfile(Screen);
            object? questCtrl = NativeBinder.GetTsgQuestController(Screen);
            object? invCtrl = NativeBinder.GetTsgInventory(Screen);

            if (DialogOpener.TryOpenOutOfRaid(TraderId, profile, questCtrl, invCtrl, out string error))
                Plugin.Instance.StartCoroutine(DialogRunner.Begin(tree, fromMenu: true));
            else
                Plugin.Log.LogWarning("[TalkButton] open failed: " + error);
        }
    }
}
