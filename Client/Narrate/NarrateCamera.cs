using System;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using EFT.UI;
using HarmonyLib;
using UnityEngine;

namespace VisitAPI.Native;

[HarmonyPatch(typeof(CameraManager), nameof(CameraManager.SetCameraFromSettings))]
public static class NarrateCameraBypass
{
    // 09-25：原来的设置项 Narrate.Fov（默认 50）/ LevelCamera（默认开）去掉了，固定成默认值
    const float DefaultFov = 50f;

    internal static float FixedFov => NarrateSpawnGuard.MarkerUsed ? NarrateSpawnGuard.Fov11 : DefaultFov;

    internal static Quaternion Level(Quaternion eye) =>
        !NarrateSpawnGuard.MarkerUsed ? Quaternion.Euler(0f, eye.eulerAngles.y, 0f) : eye;

    static bool Prefix(CameraManager __instance, CameraManager.ISettings settings)
    {
        if (!Narrating.Now) return true;
        var prefab = Resources.Load<GameObject>("Cam2_fps_hideout");
        if (prefab == null)
        {
            Plugin.Log.LogError("[narrate] compatible camera prefab 'Cam2_fps_hideout' missing");
            return true;
        }
        __instance.SetCameraFromPrefab(prefab, settings?.PrismPresetPrefab, settings?.PostProcessProfilePrefab);
        try
        {
            var camera = __instance.Camera;
            var player = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer : null;
            if (player != null) VisitCameraPin.PinNow(player);
            var eye = player != null ? player.CameraPosition : null;
            if (camera != null)
            {
                if (eye != null) camera.transform.SetPositionAndRotation(eye.position, Level(eye.rotation));
                camera.fieldOfView = FixedFov;
                __instance.Fov = FixedFov;
                __instance.SetFov(FixedFov, 0.05f);
                if (camera.GetComponent("CinemachineBrain") is Behaviour brain) brain.enabled = false;
                if (camera.GetComponent<NarrateFovEnforcer>() == null) camera.gameObject.AddComponent<NarrateFovEnforcer>();
                PrismTransplant.Apply(settings?.CameraPrefab, camera);
                Camera11.Apply(camera);
            }
            Visibility.Environment(false);
        }
        catch (Exception e) { Plugin.Log.LogError("[narrate] Failed to transfer settings after the camera was built (camera kept, effects may be incomplete): " + e); }
        return false;
    }
}

[HarmonyPatch(typeof(CameraManager), nameof(CameraManager.ApplyFoV))]
public static class NarrateFovLock
{
    static void Prefix(ref int __0)
    {
        if (!Narrating.Now) return;
        __0 = (int)Math.Round(NarrateCameraBypass.FixedFov);
    }
}

public class NarrateFovEnforcer : MonoBehaviour
{
    float _logAt;
    Camera _cam;

    void OnEnable()
    {
        _cam = GetComponent<Camera>(); Camera.onPreCull += Pin; Camera.onPreRender += PreRender;
    }
    void OnDisable() { Camera.onPreCull -= Pin; Camera.onPreRender -= PreRender; }

    float _renderLogAt;
    void PreRender(Camera cam)
    {
        if (cam != _cam || !Narrating.Now || Time.unscaledTime < _renderLogAt) return;
        var m11 = cam.projectionMatrix.m11;
        var projFov = m11 != 0f ? 2f * Mathf.Atan(1f / m11) * Mathf.Rad2Deg : 0f;
        if (Math.Abs(cam.fieldOfView - NarrateCameraBypass.FixedFov) <= 0.5f && Math.Abs(projFov - NarrateCameraBypass.FixedFov) <= 0.5f) return;
        cam.fieldOfView = NarrateCameraBypass.FixedFov;
        if (Math.Abs(projFov - NarrateCameraBypass.FixedFov) > 0.5f) cam.ResetProjectionMatrix();
        var fixedM11 = cam.projectionMatrix.m11;
        var fixedFov = fixedM11 != 0f ? 2f * Mathf.Atan(1f / fixedM11) * Mathf.Rad2Deg : 0f;
        _renderLogAt = Time.unscaledTime + 5f;
        var cm = EFT.CameraControl.CameraManager.Instance;
        Plugin.Log.LogWarning($"[narrate] Pre-render: projection matrix FOV={projFov:0.##} -> {fixedFov:0.##} after recompute (CameraManager.Fov={(cm != null ? cm.Fov : 0f):0.##} target {NarrateCameraBypass.FixedFov:0.##})");
    }

    void Pin(Camera cam)
    {
        if (cam != _cam || !Narrating.Now) return;
        Enforce(cam, "onPreCull");
    }

    void LateUpdate()
    {
        if (!Narrating.Now) return;
        if (_cam == null) _cam = GetComponent<Camera>();
        if (_cam == null) return;
        Enforce(_cam, "LateUpdate");
        var cm = EFT.CameraControl.CameraManager.Instance;
        if (cm != null && Math.Abs(cm.Fov - NarrateCameraBypass.FixedFov) > 0.5f) cm.Fov = NarrateCameraBypass.FixedFov;
    }

    void Enforce(Camera cam, string where)
    {
        var f = cam.fieldOfView;
        if (Math.Abs(f - NarrateCameraBypass.FixedFov) <= 0.5f) return;
        if (Time.unscaledTime >= _logAt)
        {
            _logAt = Time.unscaledTime + 5f;
            Plugin.Log.LogWarning($"[narrate] FOV was externally set to {f:0.##} (detected in {where}), restoring configured value {NarrateCameraBypass.FixedFov:0.##}");
        }
        cam.fieldOfView = NarrateCameraBypass.FixedFov;
    }
}

[HarmonyPatch(typeof(CameraManager), nameof(CameraManager.SetFov))]
public static class NarrateSetFovLock
{
    static void Prefix(ref float x)
    {
        if (!Narrating.Now) return;
        x = NarrateCameraBypass.FixedFov;
    }
}
