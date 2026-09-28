using System.Collections.Generic;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VisitAPI.Native;

[HarmonyPatch(typeof(NarrateGame), "Move")]
public static class NarrateSpawnGuard
{
    const float EyeHeight = 1.5f;

    public const float Fov11 = 55f;

    /// 1.1 的访问玩家不进房间：1.1MCP 实测一直在 (0,-100,0)。照放，脚下垫一块看不见的地板（0.16 的玩家没地板会往下掉）
    static readonly Vector3 HiddenSpot = new(0f, -100f, 0f);

    static readonly HashSet<string> _markerScenes = new();
    static string _current;
    public static bool MarkerUsed => _current != null && _markerScenes.Contains(_current);

    static void Prefix(NarrateGame __instance, NarrateSceneInfo sceneInfo)
    {
        var scene = SceneManager.GetSceneByName(sceneInfo.sceneName);
        _current = sceneInfo.sceneName;
        // 09-26 照 1.1 定机位（1.1MCP 在 1.1 的 Prapor 访问里实测）：1.1 删掉了 0.16 这张写死坐标的表（VendorScenePresets 只剩场景名），
        // NarrateGame.Move 改读房间里原生的机位点 StaticCameraObservationPoint——玩家本人留在地图下，玩家骨骼里的相机挂点 CameraContainer
        // 每帧钉在机位点上（位置、朝向都一样），相机照原生跟随挂点下的 Cam（挂点里右 / 上 4 厘米、前 5 厘米）；鼠标不转视角。
        // 以前这里要么用 0.16 的原生坐标（Prapor 这类房间没挪的，玩家站进房间、相机跟眼睛，比 1.1 偏约 0.3 米、低头 3.76°），
        // 要么按机位点往下 1.5 米放玩家再看向前方 5 米（挪过的房间，差几厘米）
        var point = SceneLoader.FindObservationPoint(scene);
        if (point != null)
        {
            var hidden = HiddenSpot;
            var flat = Vector3.ProjectOnPlane(point.forward, Vector3.up);
            sceneInfo.playerPosition = hidden;
            sceneInfo.targetPosition = hidden + (flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward) * 5f;
            _markerScenes.Add(_current);
            CollisionAligner.HiddenFloor(scene, hidden);
            VisitCameraPin.Arm(__instance, point);
            return;
        }
        var native = !_markerScenes.Contains(_current) && (sceneInfo.playerPosition != Vector3.zero || sceneInfo.targetPosition != Vector3.zero);
        var nativePlayer = sceneInfo.playerPosition;
        if (native && CollisionAligner.HasSupport(scene, nativePlayer))
        {
            CollisionAligner.Align(scene, nativePlayer);
            return;
        }
        var cam = SceneLoader.FindCameraPoint(scene);
        if (cam == null)
        {
            if (native) { CollisionAligner.Align(scene, nativePlayer); Plugin.Log.LogWarning($"[narrate] 0.16 native coordinates {nativePlayer} have no room underfoot and the scene has no camera marker - falling back to native coordinates"); }
            else Plugin.Log.LogWarning("[narrate] no camera point in scene - native coordinates in effect");
            return;
        }
        sceneInfo.playerPosition = cam.position - Vector3.up * EyeHeight;
        sceneInfo.targetPosition = cam.position + cam.forward * 5f;
        _markerScenes.Add(_current);
        CollisionAligner.Align(scene, sceneInfo.playerPosition);
    }
}

/// <summary>09-26 照 1.1：访问期间每帧把访问玩家的相机挂点 CameraContainer 钉在房间机位点上（见 NarrateSpawnGuard）。
/// 挂在原生 PlayerCameraController.LateUpdate 前面：动画在 LateUpdate 之前已经算完，钉完这一帧原生 FirstPersonCameraState 接着按挂点下的 Cam 摆相机。
/// NPC 看的也是这个挂点（原生 NarrateController 给 GoIn 传的就是 CameraContainer），钉住后商人看向镜头，和 1.1 一样</summary>
[HarmonyPatch(typeof(PlayerCameraController), nameof(PlayerCameraController.LateUpdate))]
public static class VisitCameraPin
{
    static NarrateGame _game;
    static Transform _point;

    /// 09-26（SORA：访问后进藏身处第一人称，人物飞天）：原生从不复位 CameraContainer（只在建玩家时 FindTransform 找到它），
    /// 钉过的挂点离玩家身体约 140 米、朝向也改了，访问玩家回对象池后被藏身处玩家复用，第一人称相机就挂在天上。
    /// 1.1 访问后不回收访问玩家（隐藏后复用），碰不到这个；我们每次访问都拆，所以访问一结束就把挂点还原成第一次钉之前的本地位置和朝向
    static readonly Dictionary<Transform, (Vector3 pos, Quaternion rot)> _home = new();
    static Transform _pinned;

    internal static void Arm(NarrateGame game, Transform point)
    {
        _game = game;
        _point = point;
    }

    internal static void Clear()
    {
        _game = null;
        _point = null;
        Restore();
    }

    static void Restore()
    {
        var container = _pinned;
        _pinned = null;
        if (container == null || !_home.TryGetValue(container, out var home)) return;
        container.localPosition = home.pos;
        container.localRotation = home.rot;
    }

    /// 相机刚建好（NarrateCameraBypass）时先钉一次，第一帧相机就在机位上
    internal static void PinNow(Player player) => Pin(player);

    static void Prefix(PlayerCameraController __instance) => Pin(__instance.Player);

    static void Pin(Player player)
    {
        if (_point == null || _game == null || player == null) return;
        var owner = _game.PlayerOwner;
        if (owner == null || owner.Player != player) return;
        var container = player.CameraContainer != null ? player.CameraContainer.transform : null;
        if (container == null) return;
        if (!_home.ContainsKey(container)) _home[container] = (container.localPosition, container.localRotation);
        _pinned = container;
        container.SetPositionAndRotation(_point.position, _point.rotation);
    }
}

static class CollisionAligner
{
    internal static bool HasSupport(Scene scene, Vector3 spawn) => scene.isLoaded && FindSupport(scene, spawn, out _) != null;

    static Collider FindSupport(Scene scene, Vector3 spawn, out RaycastHit supportHit)
    {
        var ray = new Ray(spawn + Vector3.up * 3f, Vector3.down);
        Collider support = null;
        supportHit = default;
        var nearest = float.PositiveInfinity;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger) continue;
                if (collider.Raycast(ray, out var hit, 20f) && hit.distance < nearest)
                {
                    support = collider;
                    supportHit = hit;
                    nearest = hit.distance;
                }
            }
        return support;
    }

    /// 藏在房间下面的访问玩家脚下垫一块地板，免得一直往下掉（地板放进房间场景，随房间一起卸掉）
    internal static void HiddenFloor(Scene scene, Vector3 spawn)
    {
        if (!scene.isLoaded) return;
        if (FindSupport(scene, spawn, out _) != null) return;
        Floor(scene, spawn, quiet: true);
    }

    internal static void Align(Scene scene, Vector3 spawn)
    {
        if (!scene.isLoaded) return;
        var support = FindSupport(scene, spawn, out var supportHit);
        if (support == null) { Floor(scene, spawn); return; }
        var offset = spawn.y - supportHit.point.y;
        if (Mathf.Abs(offset) <= 0.05f) return;
        if (Mathf.Abs(offset) > 20f) { Plugin.Log.LogWarning($"[narrate] support collider offset rejected: {support.name} dy={offset:0.###}"); return; }
        support.transform.position += Vector3.up * offset;
        Physics.SyncTransforms();
    }

    static void Floor(Scene scene, Vector3 spawn, bool quiet = false)
    {
        var go = new GameObject("VisitAPI_Floor");
        go.layer = FloorLayer();
        go.AddComponent<BoxCollider>().size = new Vector3(40f, 0.2f, 40f);
        go.transform.position = spawn + Vector3.down * 0.1f;
        SceneManager.MoveGameObjectToScene(go, scene);
        Physics.SyncTransforms();
        if (!quiet) Plugin.Log.LogWarning($"[narrate] no support collider below spawn {spawn} in '{scene.name}' - invisible floor added (layer {LayerMask.LayerToName(go.layer)})");
    }

    static int FloorLayer()
    {
        var player = LayerMask.NameToLayer("Player");
        foreach (var name in new[] { "LowPolyCollider", "HighPolyCollider", "Terrain", "Default" })
        {
            var layer = LayerMask.NameToLayer(name);
            if (layer >= 0 && (player < 0 || !Physics.GetIgnoreLayerCollision(player, layer))) return layer;
        }
        return 0;
    }
}
