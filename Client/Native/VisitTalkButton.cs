using EFT.UI;
using UnityEngine;

namespace VisitAPI.Native
{
    // Tags THE single visit entry button on the out-of-raid trade screen and carries the live
    // TraderScreensGroup + the currently-selected trader id. The entry patch re-points these on every trader
    // selection; the click reads them live and opens the trader's `.dlg` dialog.
    internal sealed class VisitTalkButton : MonoBehaviour
    {
        internal object? Screen;
        internal string TraderId = "";
        internal DefaultUIButton? Button;

        internal void Configure(object screen, string traderId)
        {
            Screen = screen;
            TraderId = traderId ?? "";
            if (Button != null) Button.SetRawText(Loc.DefaultTalkLabel, 24);
        }

        internal void OnTalkClicked()
        {
            if (Screen == null || string.IsNullOrEmpty(TraderId)) return;
            NativeBinder.ActiveTradeScreen = Screen;

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
