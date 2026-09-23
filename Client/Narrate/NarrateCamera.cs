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
    internal static float FixedFov => NarrateSpawnGuard.MarkerUsed ? NarrateSpawnGuard.Fov11 : Plugin.Fov.Value;

    internal static Quaternion Level(Quaternion eye) =>
        Plugin.LevelCamera.Value && !NarrateSpawnGuard.MarkerUsed ? Quaternion.Euler(0f, eye.eulerAngles.y, 0f) : eye;

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
            var eye = player != null ? player.CameraPosition : null;
            if (camera != null)
            {
                if (eye != null) camera.transform.SetPositionAndRotation(eye.position, Level(eye.rotation));
                camera.fieldOfView = FixedFov;
                __instance.Fov = FixedFov;
                __instance.SetFov(FixedFov, 0.05f);
                if (camera.GetComponent("CinemachineBrain") is Behaviour brain)
                {
                    string active = "无";
                    try
                    {
                        var vcam = brain.GetType().GetProperty("ActiveVirtualCamera")?.GetValue(brain);
                        if (vcam != null)
                        {
                            var name = vcam.GetType().GetProperty("Name")?.GetValue(vcam) as string;
                            var state = vcam.GetType().GetProperty("State")?.GetValue(vcam);
                            var lens = state?.GetType().GetField("Lens")?.GetValue(state);
                            var lensFov = lens?.GetType().GetField("FieldOfView")?.GetValue(lens);
                            active = $"{name} 镜头 FOV={lensFov}";
                        }
                    }
                    catch (Exception e) { active = "读取失败: " + e.Message; }
                    brain.enabled = false;
                    Plugin.Log.LogInfo($"[narrate] 访问相机上的 CinemachineBrain 已关（之前驱动的虚拟相机：{active}）");
                }
                if (camera.GetComponent<NarrateFovEnforcer>() == null) camera.gameObject.AddComponent<NarrateFovEnforcer>();
                PrismTransplant.Apply(settings?.CameraPrefab, camera);
                Camera11.Apply(camera);
            }
            Visibility.Environment(false);
            Plugin.Log.LogInfo($"[narrate] 访问相机建好：pos={(camera != null ? camera.transform.position.ToString() : "?")} fov={(camera != null ? camera.fieldOfView : 0f):0.##} 目标 {FixedFov:0.##} CameraManager.Fov={__instance.Fov:0.##} 机位标记={NarrateSpawnGuard.MarkerUsed}");
        }
        catch (Exception e) { Plugin.Log.LogError("[narrate] 相机建好后的参数搬运失败（相机保留，效果可能不全）: " + e); }
        return false;
    }
}

[HarmonyPatch(typeof(CameraManager), nameof(CameraManager.ApplyFoV))]
public static class NarrateFovLock
{
    static float _logAt;
    static void Prefix(ref int __0)
    {
        if (!Narrating.Now) return;
        var to = (int)Math.Round(NarrateCameraBypass.FixedFov);
        if (__0 != to && Time.unscaledTime >= _logAt) { _logAt = Time.unscaledTime + 5f; Plugin.Log.LogInfo($"[narrate] ApplyFoV({__0}) 访问期改写成 {to}"); }
        __0 = to;
    }
}

public class NarrateFovEnforcer : MonoBehaviour
{
    float _logAt;
    Camera _cam;

    void OnEnable()
    {
        _cam = GetComponent<Camera>(); Camera.onPreCull += Pin; Camera.onPreRender += PreRender;
        Plugin.Log.LogInfo($"[narrate] FOV 执法器挂上：相机 {(_cam != null ? _cam.name : "?")} 现值 {(_cam != null ? _cam.fieldOfView : 0f):0.##}，目标 {NarrateCameraBypass.FixedFov:0.##}");
        if (_cam != null)
            Plugin.Log.LogInfo("[narrate] 相机组件（带 渲染前/LateUpdate 回调的标 *）: " + string.Join(", ", _cam.GetComponents<Component>().Where(c => c != null).Select(c =>
            {
                var t = c.GetType();
                const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                var hooks = new[] { "OnPreCull", "OnPreRender", "LateUpdate" }.Where(m => t.GetMethod(m, F, null, Type.EmptyTypes, null) != null).ToArray();
                return hooks.Length > 0 ? $"{t.Name}*[{string.Join("/", hooks)}]" : t.Name;
            })));
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
        Plugin.Log.LogWarning($"[narrate] 渲染前：投影矩阵折算 FOV={projFov:0.##} → 重算后 {fixedFov:0.##}（CameraManager.Fov={(cm != null ? cm.Fov : 0f):0.##} 目标 {NarrateCameraBypass.FixedFov:0.##}）");
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
            Plugin.Log.LogWarning($"[narrate] FOV 被外力写成 {f:0.##}（{where} 时发现），按回配置值 {NarrateCameraBypass.FixedFov:0.##}");
        }
        cam.fieldOfView = NarrateCameraBypass.FixedFov;
    }
}

[HarmonyPatch(typeof(CameraManager), nameof(CameraManager.SetFov))]
public static class NarrateSetFovLock
{
    static float _logAt;
    static void Prefix(ref float x, float time)
    {
        if (!Narrating.Now) return;
        if (Math.Abs(x - NarrateCameraBypass.FixedFov) > 0.5f && Time.unscaledTime >= _logAt)
        {
            _logAt = Time.unscaledTime + 5f;
            Plugin.Log.LogInfo($"[narrate] SetFov({x:0.##}, {time:0.##}s) 访问期改写成 {NarrateCameraBypass.FixedFov:0.##}");
        }
        x = NarrateCameraBypass.FixedFov;
    }
}
