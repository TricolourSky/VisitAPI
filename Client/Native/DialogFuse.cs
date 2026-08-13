using EFT.Dialogs;
using UnityEngine;

namespace VisitAPI.Native;

public static class DialogFuse
{
    public static void Watch(ClientDialogController dc)
    {
        var count = 0; var windowStart = 0f;
        dc.OnDialogChanged += dialog =>
        {
            if (dialog == null) return;
            dialog.OnExecuteLine += _ =>
            {
                if (Time.unscaledTime - windowStart > 1f) { windowStart = Time.unscaledTime; count = 0; }
                if (++count < 25) return;
                Plugin.Log.LogError("[fuse] dialog line runaway detected - stopping dialog controller");
                count = 0; windowStart = Time.unscaledTime + 4f;
                dc.StopDialog();
                var screen = Object.FindObjectOfType<EFT.UI.TraderDialogScreen>();
                if (screen != null) screen.method_8();
            };
        };
    }
}
