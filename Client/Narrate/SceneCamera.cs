using Comfort.Common;
using EFT.CameraControl;
using EFT.UI;
using UnityEngine;

namespace VisitAPI.Native;

/// <summary>
/// 自定义 `.dlg` `scene:` 场景的专用相机（0.16 的 `Cam2_fps_hideout` 预制体）。
/// 原生 narrate 路径不建它，但退出时 `NarrateEntry.EnsureMenu` 仍会调 `Hide()`——
/// 那一次调用同时把 `CameraManager.IsActive` 和 EnvironmentUI 压回菜单状态（实机验证过的时序，别动）。
/// </summary>
public static class SceneCamera
{
    static Camera _cam;

    static Camera Ensure()
    {
        if (_cam != null) return _cam;
        var prefab = Resources.Load<GameObject>("Cam2_fps_hideout");
        if (prefab == null) { Plugin.Log.LogError("[scene] camera prefab 'Cam2_fps_hideout' missing"); return null; }
        var go = Object.Instantiate(prefab);
        go.name = "VisitSceneCamera";
        _cam = go.GetComponent<Camera>();
        if (go.GetComponent("CinemachineBrain") is Behaviour brain) brain.enabled = false;
        if (_cam.GetComponent<AudioListener>() == null) _cam.gameObject.AddComponent<AudioListener>();
        Object.DontDestroyOnLoad(go);
        return _cam;
    }

    public static void Show(Transform point)
    {
        if (Ensure() == null) return;
        Visibility.Camera(false);
        _cam.gameObject.SetActive(true);
        _cam.transform.SetPositionAndRotation(point.position, point.rotation);
        if (CameraManager.Instance != null && CameraManager.Instance.Camera != _cam)
            CameraManager.Instance.SetCamera(_cam);
        Visibility.Environment(false);
    }

    public static void Hide()
    {
        if (_cam != null) _cam.gameObject.SetActive(false);
        Visibility.Camera(true);
        Visibility.Environment(true);
    }
}
