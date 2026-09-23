using Comfort.Common;
using EFT.CameraControl;
using EFT.UI;

namespace VisitAPI.Native;

public static class Visibility
{
    public static void Camera(bool active)
    {
        var cm = CameraManager.Instance;
        if (cm == null || cm.Camera == null || cm.IsActive == active) return;
        cm.IsActive = active;
        Plugin.Log.LogDebug("[vis] camera " + (active ? "on" : "off"));
    }

    public static void Environment(bool shown)
    {
        var ui = Singleton<EnvironmentUI>.Instantiated ? Singleton<EnvironmentUI>.Instance : null;
        if (ui != null) ui.ShowEnvironment(shown);
    }
}
