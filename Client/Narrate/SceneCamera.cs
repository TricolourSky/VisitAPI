using System.Collections;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using UnityEngine;

namespace VisitAPI.Native;

/// <summary>藏身处第一人称相机的预制体：访问（NarrateCameraBypass）和 .dlg 的房间机位（SceneCamera）都拿它当底。
/// 预制体没有就 null，调用方自己记日志。</summary>
static class HideoutCam
{
    public const string PrefabName = "Cam2_fps_hideout";

    public static GameObject Prefab => Resources.Load<GameObject>(PrefabName);

    /// 这台相机上的 CinemachineBrain 关掉（09-16 #137：它会抢着改访问期的 FOV）
    public static void Tame(Camera cam)
    {
        if (cam != null && cam.GetComponent("CinemachineBrain") is Behaviour brain) brain.enabled = false;
    }
}

/// <summary>.dlg 的 scene: 房间机位。1.3.4 B4：CameraManager.SetCamera 第一步 Reset() 会 SafeDestroy 当前绑定相机的整个物体，
/// 所以换上房间相机的那一帧藏身处相机就没了，「关掉时绑回原相机」永远绑不回去；而且玩家身上的 PlayerCameraController 还对着那台已销毁的相机。
/// 这里照原生藏身处切相机的顺序（HideoutController.method_11 / method_10）：
///   打开：玩家身上有相机控制器（藏身处）就先 PlayerCameraController.Destroy，再用原生 SetCameraFromPrefab 建房间相机；
///   关闭：隔一帧 PlayerCameraController.Create（Construct 里按 LevelSettings 重建藏身处相机，Reset 顺手销毁房间相机）→ IsActive = true。
/// 主菜单里没有相机控制器：关掉时只把房间相机收起，MenuScreen 的 PrepareEnvironment 接管环境。</summary>
public static class SceneCamera
{
    static Camera _cam;
    static Player _player;
    static LevelSettings _levelSettings;
    static bool _shown;

    public static void Show(Transform point)
    {
        var cm = CameraManager.Instance;
        if (cm == null) { Plugin.Log.LogWarning("[scene] no CameraManager, room camera skipped"); return; }
        if (!_shown || _cam == null)
        {
            var prefab = HideoutCam.Prefab;
            if (prefab == null) { Plugin.Log.LogError("[scene] camera prefab 'Cam2_fps_hideout' missing"); return; }
            _levelSettings = Singleton<LevelSettings>.Instantiated ? Singleton<LevelSettings>.Instance : null;
            _player = HostPlayer();
            if (_player != null) PlayerCameraController.Destroy(_player);
            cm.SetCameraFromPrefab(prefab);
            _cam = cm.Camera;
            if (_cam == null) { Plugin.Log.LogError("[scene] native SetCameraFromPrefab gave no camera"); return; }
            _cam.gameObject.name = "VisitSceneCamera";
            HideoutCam.Tame(_cam);
            _shown = true;
        }
        _cam.gameObject.SetActive(true);
        _cam.transform.SetPositionAndRotation(point.position, point.rotation);
        Visibility.Environment(false);
    }

    /// 在藏身处（玩家身上挂着相机控制器）时返回那个玩家；主菜单、访问里返回 null
    static Player HostPlayer()
    {
        if (Narrating.Now) return null;
        var player = Raid.MainPlayer;
        return player != null && player.gameObject.GetComponent<PlayerCameraController>() != null ? player : null;
    }

    public static void Hide()
    {
        if (!_shown)
        {
            // 没有经过 Show（访问收尾走 EnsureMenu 时）
            if (_cam != null) _cam.gameObject.SetActive(false);
            Visibility.Camera(true);
            Visibility.Environment(true);
            return;
        }
        _shown = false;
        var player = _player;
        _player = null;
        if (player != null) Plugin.Instance.StartCoroutine(RestorePlayerCamera(player, _levelSettings));
        else if (_cam != null) _cam.gameObject.SetActive(false);
        _levelSettings = null;
        Visibility.Environment(true);
    }

    /// 原生 Destroy 是延迟销毁，同一帧 Create 会看到组件还在就直接返回——隔一帧再建
    static IEnumerator RestorePlayerCamera(Player player, LevelSettings levelSettings)
    {
        yield return null;
        if (player == null) { Plugin.Log.LogWarning("[scene] host player gone, player camera not restored"); yield break; }
        try
        {
            if (levelSettings != null && (!Singleton<LevelSettings>.Instantiated || Singleton<LevelSettings>.Instance != levelSettings))
                Singleton<LevelSettings>.Create(levelSettings);
            PlayerCameraController.Create(player);
            if (CameraManager.Instance != null) CameraManager.Instance.IsActive = true;
            _cam = null;
        }
        catch (System.Exception e) { Plugin.Log.LogError("[scene] player camera rebuild failed: " + e); }
    }
}
