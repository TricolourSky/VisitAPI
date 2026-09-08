using Comfort.Common;
using EFT.CameraControl;
using EFT.UI;

namespace VisitAPI.Native;

/// <summary>`CameraManager.IsActive` 与 `EnvironmentUI.ShowEnvironment` 的**唯一写入点**（Refactor_Plan 承诺的 Visibility 权威）。
/// 旧病是 5 个文件各写各的开关——现在别处一律只许调这里。各调用方的**调用顺序原样保留**（M6 铁律④），
/// 这里只做集中与留痕，不做任何时序裁决。</summary>
public static class Visibility
{
    /// 相机开关。已是目标值就不再写（setter 有副作用，重复写纯浪费）；没绑相机时 CameraSafety 的补丁本就会拦。
    public static void Camera(bool active)
    {
        var cm = CameraManager.Instance;
        if (cm == null || cm.Camera == null || cm.IsActive == active) return;
        cm.IsActive = active;
        Plugin.Log.LogDebug("[vis] camera " + (active ? "on" : "off"));
    }

    /// 菜单 3D 环境显隐。访问/自定义场景把它压下去，退出还回来。语义与原生一致：无实例时静默跳过。
    public static void Environment(bool shown)
    {
        // EnvironmentUI 是 MonoBehaviour：显式判 Unity 空，不用 `?.`（09-07 终审）
        var ui = Singleton<EnvironmentUI>.Instantiated ? Singleton<EnvironmentUI>.Instance : null;
        if (ui != null) ui.ShowEnvironment(shown);
    }
}
