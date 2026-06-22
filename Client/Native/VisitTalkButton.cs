using UnityEngine;

namespace VisitAPI.Native
{
    // Tags the cloned 对话 button on the out-of-raid trade screen and carries the live TraderScreensGroup + the
    // currently-selected trader id. The entry patch re-points these on every trader selection; the click reads them
    // live so the button always opens the dialog for whichever trader is shown.
    internal sealed class VisitTalkButton : MonoBehaviour
    {
        internal object? Screen;
        internal string TraderId = "";

        internal void Configure(object screen, string traderId)
        {
            Screen = screen;
            TraderId = traderId ?? "";
        }

        internal void OnTalkClicked()
        {
            if (Screen == null || string.IsNullOrEmpty(TraderId)) return;

            DialogTree? tree = DialogTreeLoader.Load(TraderId);
            if (tree == null)
            {
                Plugin.Log.LogWarning("[TalkButton] no .dlg tree for " + TraderId);
                return;
            }

            object? profile = NativeBinder.GetTsgProfile(Screen);
            object? questCtrl = NativeBinder.GetTsgQuestController(Screen);
            object? invCtrl = NativeBinder.GetTsgInventory(Screen);
            NativeBinder.ActiveTradeScreen = Screen;

            if (DialogOpener.TryOpenOutOfRaid(TraderId, profile, questCtrl, invCtrl, out string error))
                Plugin.Instance.StartCoroutine(DialogRunner.Begin(tree, fromMenu: true));
            else
                Plugin.Log.LogWarning("[TalkButton] open failed: " + error);
        }
    }
}
