using System;
using EFT.CameraControl;
using HarmonyLib;
using UnityEngine;

namespace VisitAPI.Native;

[HarmonyPatch(typeof(CameraManager), nameof(CameraManager.SetCameraFromSettings))]
public static class NarrateCameraBypass
{
    const float DefaultFov = 50f;

    internal static float FixedFov => NarrateSpawnGuard.MarkerUsed ? NarrateSpawnGuard.Fov11 : DefaultFov;

    internal static Quaternion Level(Quaternion eye) =>
        !NarrateSpawnGuard.MarkerUsed ? Quaternion.Euler(0f, eye.eulerAngles.y, 0f) : eye;

    static bool Prefix(CameraManager __instance, CameraManager.ISettings settings)
    {
        if (!Narrating.Now) return true;
        var prefab = HideoutCam.Prefab;
        if (prefab == null)
        {
            Plugin.Log.LogError("[narrate] compatible camera prefab 'Cam2_fps_hideout' missing");
            return true;
        }
        __instance.SetCameraFromPrefab(prefab, settings?.PrismPresetPrefab, settings?.PostProcessProfilePrefab);
        try
        {
            var camera = __instance.Camera;
            var player = Raid.MainPlayer;
            if (player != null) VisitCameraPin.Pin(player);
            var eye = player != null ? player.CameraPosition : null;
            if (camera != null)
            {
                if (eye != null) camera.transform.SetPositionAndRotation(eye.position, Level(eye.rotation));
                camera.fieldOfView = FixedFov;
                __instance.Fov = FixedFov;
                __instance.SetFov(FixedFov, 0.05f);
                HideoutCam.Tame(camera);
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
    /// 渲染前再看一眼投影矩阵（#137）：每帧都修，日志 5 秒一条（10-29：以前节流把修正也一起跳过了，修过一次 5 秒内不再看）
    void PreRender(Camera cam)
    {
        if (cam != _cam || !Narrating.Now) return;
        var projFov = ProjFov(cam.projectionMatrix.m11);
        if (Math.Abs(cam.fieldOfView - NarrateCameraBypass.FixedFov) <= 0.5f && Math.Abs(projFov - NarrateCameraBypass.FixedFov) <= 0.5f) return;
        cam.fieldOfView = NarrateCameraBypass.FixedFov;
        if (Math.Abs(projFov - NarrateCameraBypass.FixedFov) > 0.5f) cam.ResetProjectionMatrix();
        if (Time.unscaledTime < _renderLogAt) return;
        var fixedFov = ProjFov(cam.projectionMatrix.m11);
        _renderLogAt = Time.unscaledTime + 5f;
        var cm = EFT.CameraControl.CameraManager.Instance;
        Plugin.Log.LogWarning($"[narrate] Pre-render: projection matrix FOV={projFov:0.##} -> {fixedFov:0.##} after recompute (CameraManager.Fov={(cm != null ? cm.Fov : 0f):0.##} target {NarrateCameraBypass.FixedFov:0.##})");
    }

    /// 由投影矩阵的 m11 折算实际生效的竖直 FOV（度）
    static float ProjFov(float m11) => m11 != 0f ? 2f * Mathf.Atan(1f / m11) * Mathf.Rad2Deg : 0f;

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
        var cm = CameraManager.Instance;
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
