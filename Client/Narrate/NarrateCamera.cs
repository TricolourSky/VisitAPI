using System;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using EFT.UI;
using HarmonyLib;
using UnityEngine;

namespace VisitAPI.Native;

/// <summary>
/// 访问期间不用玩家设置建相机：1.1 场景相机预制体在 0.16 只有 6 个可绑定组件，整搬会黑屏（实机证实）。
/// 用 0.16 自己的 `Cam2_fps_hideout` 完整效果链顶上，仍由原生 SetCameraFromPrefab 完成实例化与效果登记。
/// FOV 走配置 `Narrate.Fov`：09-02 定 50 → 09-04 SORA 改 55 → 同日改回 1.1 相机预制体的 75 做影调对照（自动曝光按画面内容取样，
/// 视野变了取样内容也变）。默认值即当前生效值，改动需 SORA 拍板。
/// </summary>
[HarmonyPatch(typeof(CameraManager), nameof(CameraManager.SetCameraFromSettings))]
public static class NarrateCameraBypass
{
    /// 按 1.1 机位标记定位的房间用 1.1 写死的 55 度；走 0.16 原生坐标的（Prapor）用配置值
    internal static float FixedFov => NarrateSpawnGuard.MarkerUsed ? NarrateSpawnGuard.Fov11 : Plugin.Fov.Value;

    /// <summary>玩家眼睛带着 3.76° 下俯，1.1 房间自带的机位标记写的是俯仰 0——把两张图的人物位置逐像素量过：
    /// 这 3.76° 正好等于 1.1 里人物高出我们的那 80 像素（坑 #118 修正版：机位标记的**坐标**是错的，**朝向**是对的）。
    /// 只把俯仰和翻滚归零，朝向（yaw）和位置仍用玩家眼睛的。</summary>
    internal static Quaternion Level(Quaternion eye) =>
        Plugin.LevelCamera.Value && !NarrateSpawnGuard.MarkerUsed ? Quaternion.Euler(0f, eye.eulerAngles.y, 0f) : eye;   // 按 1.1 机位标记定位的房间保留标记自带的俯仰

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
        // 相机已经按我们的预制体建好了：后面无论哪一步抛异常都必须 return false，否则异常从前缀漏回 PlayerCameraController、
        // 引擎再按原逻辑建第二台相机（09-07 终审）。参数搬运 / 效果名单本就是「能装多少装多少」，失败只记日志。
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
                // 09-08 SORA 实机（藏身处 → 访问）：藏身处玩家举枪瞄准时 Player.Look 发过 SetFov(设置值-15=40, 1s)，那条渐变协程跑在 StaticManager 上、
                // 每帧重读 CameraManager.Camera——相机换成我们的之后它继续把新相机拉向 40 并把 CameraManager.Fov 记成 40；健康效果的 FOV_Accumulator
                // 又每帧按 Fov+ε 写回 → 商人贴脸。协程起于访问之前，SetFov 前缀改不到它的目标值；这里再调一次 SetFov 把它杀掉（前缀会把目标改成 FixedFov）。
                __instance.SetFov(FixedFov, 0.05f);
                if (camera.GetComponent<NarrateFovEnforcer>() == null) camera.gameObject.AddComponent<NarrateFovEnforcer>();
                // 后处理参数整套搬 1.1 相机预制体上的 PrismEffects（包内原生数据），插件不发明数值
                PrismTransplant.Apply(settings?.CameraPrefab, camera);
                Camera11.Apply(camera);   // 1.1 名单：本机多出来的效果关掉，1.1 开着的按预制体参数灌上
            }
            Visibility.Environment(false);
            Plugin.Log.LogDebug($"[narrate] native compatible camera registered: pos={(camera != null ? camera.transform.position.ToString() : "?")} fov={(camera != null ? camera.fieldOfView : 0f):0.##}");
        }
        catch (Exception e) { Plugin.Log.LogError("[narrate] 相机建好后的参数搬运失败（相机保留，效果可能不全）: " + e); }
        return false;
    }
}

// CameraManager 会把玩家设置重新写进相机；商人访问期间把这个入口固定住。
[HarmonyPatch(typeof(CameraManager), nameof(CameraManager.ApplyFoV))]
public static class NarrateFovLock
{
    static void Prefix(ref int __0)
    {
        if (Narrating.Now) __0 = (int)Math.Round(NarrateCameraBypass.FixedFov);
    }
}

/// <summary>第三道 FOV 防线：有东西绕过 CameraManager 直写相机（实测被写到 41.85，视角像凑近了）。
/// 访问期逐帧执法——偏离配置值超过 0.5 度就按回去并记下现行值。随相机销毁自灭。</summary>
public class NarrateFovEnforcer : MonoBehaviour
{
    float _logAt;
    Camera _cam;

    // 09-08：除了 LateUpdate，再挂一道 onPreCull（每台相机真正渲染前的最后一刻）——Update/LateUpdate 阶段之后还有人写 FOV 的话，
    // 这里是最后一次机会；同时把 CameraManager.Fov 一起钉住（FOV_Accumulator 之类是按「Fov + ε」写回相机的，基数不对怎么按都按不住）
    void OnEnable() { _cam = GetComponent<Camera>(); Camera.onPreCull += Pin; }
    void OnDisable() { Camera.onPreCull -= Pin; }

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

// 第二条 FOV 写入口：SetFov(float, time) 是带渐变协程的版本。
// ProceduralWeaponAnimation（HideWeapon → InitTransforms 等）会用它把玩家 FOV(≈68) 平滑写回相机——
// 二次进入实机抓到 fov=67.62 正是这条渐变的中途值（首次进入时它跑在相机创建前，被引擎空检查挡掉）。
[HarmonyPatch(typeof(CameraManager), nameof(CameraManager.SetFov))]
public static class NarrateSetFovLock
{
    static void Prefix(ref float x)
    {
        if (Narrating.Now) x = NarrateCameraBypass.FixedFov;
    }
}
