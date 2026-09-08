using System.Collections.Generic;
using EFT;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VisitAPI.Native;

/// <summary>
/// 进房坐标链（2026-09-06 按第一性原理重定）：
/// 1.1 每间房都带一个 `StaticCameraObservationPoint`（挂在 `Position_Camera_<商人>` 上），1.1 的 `NarrateGame.Move()` 就是把相机放到它那儿——
/// 那是 1.1 自己的定位数据。8 间房逐个核过：标记距 NPC 1.2～1.7 m、朝向正对 NPC（Peacekeeper 3.95 m / 0.92），唯独 Prapor 的标记朝向反着。
/// 0.16 引擎写死的 `VendorScenePresets` 坐标是 0.16 那版房间的（Therapist (146.7,0.2,0.9)、Mechanic (88.6,0.1,-3.4)、Fence (48.7,0.2,2)），
/// 1.1 把这几间房整个挪到了原点附近、离地 40 m 的位置，只有 Prapor 没挪——按 0.16 坐标传送 = 站在虚空里对着黑屏（D:\EFT 实测 Therapist / Mechanic）。
/// 规矩：原生坐标脚下**真有房间**（射线打得到承托面）才照用（Prapor，实机验收过的构图不动）；否则一律按 1.1 机位标记定位，
/// 朝向用标记自己的 forward。承托 collider 的 Y 校正照旧（Prapor 的承托面比可见地面低 10.712 m 是资产错位，两阶段探针实证）。
/// </summary>
[HarmonyPatch(typeof(NarrateGame), "Move")]
public static class NarrateSpawnGuard
{
    const float EyeHeight = 1.5f;   // 实机：Prapor 生成点 34.30 → 相机 35.77，Fence 39.91 → 41.40

    /// <summary>1.1 `NarrateGame` 里写死的访问相机视野（`private const float FOV = 55f`）。按 1.1 机位标记定位的房间连视野一起照 1.1 走，
    /// 09-06 拿 SORA 的 Therapist / Fence 截图和 1.1 正式版逐像素量过：标记位 + 55 度，人物纵向尺寸对得上；配置里的 `Narrate.Fov` 只管
    /// 走 0.16 原生坐标那条路（Prapor，50 是拿 0.16 坐标校出来的构图值）。</summary>
    public const float Fov11 = 55f;

    /// <summary>按 1.1 机位标记定过位的场景（引擎的 NarrateSceneInfo 是同一个对象，改过一次坐标后二次进入就被当成「原生坐标」，得记住）。
    /// 相机俯仰归零（`NarrateCameraBypass.Level`）只对 0.16 原生坐标那条路做——那是给 Prapor 校的；标记自带俯仰（Therapist 低头 8° 看坐着的大妈），归零反而错。</summary>
    static readonly HashSet<string> _markerScenes = new();
    static string _current;
    public static bool MarkerUsed => _current != null && _markerScenes.Contains(_current);

    static void Prefix(NarrateSceneInfo sceneInfo)
    {
        var scene = SceneManager.GetSceneByName(sceneInfo.sceneName);
        _current = sceneInfo.sceneName;
        var native = !_markerScenes.Contains(_current) && (sceneInfo.playerPosition != Vector3.zero || sceneInfo.targetPosition != Vector3.zero);
        var nativePlayer = sceneInfo.playerPosition;
        if (native && CollisionAligner.HasSupport(scene, nativePlayer))
        {
            CollisionAligner.Align(scene, nativePlayer);
            Plugin.Log.LogInfo($"[narrate] 0.16 原生坐标脚下有房间，照用: player={nativePlayer} target={sceneInfo.targetPosition}");
            return;
        }
        var cam = SceneLoader.FindCameraPoint(scene);
        if (cam == null)
        {
            if (native) { CollisionAligner.Align(scene, nativePlayer); Plugin.Log.LogWarning($"[narrate] 0.16 原生坐标 {nativePlayer} 脚下没有房间，场景里也没有机位标记——只能照用原生坐标"); }
            else Plugin.Log.LogWarning("[narrate] no camera point in scene - native coordinates in effect");
            return;
        }
        sceneInfo.playerPosition = cam.position - Vector3.up * EyeHeight;   // 标记 = 相机眼位
        sceneInfo.targetPosition = cam.position + cam.forward * 5f;
        _markerScenes.Add(_current);
        CollisionAligner.Align(scene, sceneInfo.playerPosition);
        Plugin.Log.LogInfo(native
            ? $"[narrate] 0.16 原生坐标 {nativePlayer} 脚下没有房间（1.1 把这间房挪了位置），改按 1.1 机位标记定位: {cam.position} 朝向 {cam.forward}"
            : $"[narrate] 按 1.1 机位标记定位: {cam.position} 朝向 {cam.forward}");
    }
}

/// <summary>只校正生成点正下方射线命中的那一个承托 collider 的世界 Y；不碰玩家/相机/其他碰撞体。</summary>
static class CollisionAligner
{
    /// <summary>生成点脚下 20 m 内有没有承托面（判「0.16 原生坐标是不是落在这间房里」用）。</summary>
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

    internal static void Align(Scene scene, Vector3 spawn)
    {
        if (!scene.isLoaded) return;
        var support = FindSupport(scene, spawn, out var supportHit);
        if (support == null) { Floor(scene, spawn); return; }
        var offset = spawn.y - supportHit.point.y;
        if (Mathf.Abs(offset) <= 0.05f) { Plugin.Log.LogDebug($"[narrate] support collider already aligned: {support.name} floor={supportHit.point.y:0.###}"); return; }
        if (Mathf.Abs(offset) > 20f) { Plugin.Log.LogWarning($"[narrate] support collider offset rejected: {support.name} dy={offset:0.###}"); return; }
        support.transform.position += Vector3.up * offset;
        Physics.SyncTransforms();
        Plugin.Log.LogInfo($"[narrate] support collider aligned: {support.name} dy={offset:0.###} floor={supportHit.point.y:0.###}->{spawn.y:0.###}");
    }

    /// tarkin 直打包里没有任何 collider（M4f：他剥掉了 MeshCollider），玩家一传送到位就穿地坠落，听者跟着越飘越远、
    /// 语音也听不见（坑 #101，2026-09-03 实机）。生成点脚下铺一块隐形地板，顶面正好在原生坐标的 Y 上；挂进房间场景，随卸载一起消失。
    static void Floor(Scene scene, Vector3 spawn)
    {
        var go = new GameObject("VisitAPI_Floor");
        go.layer = FloorLayer();
        go.AddComponent<BoxCollider>().size = new Vector3(40f, 0.2f, 40f);
        go.transform.position = spawn + Vector3.down * 0.1f;
        SceneManager.MoveGameObjectToScene(go, scene);
        Physics.SyncTransforms();
        Plugin.Log.LogWarning($"[narrate] no support collider below spawn {spawn} in '{scene.name}' - invisible floor added (layer {LayerMask.LayerToName(go.layer)})");
    }

    // 地板的层：按物理碰撞矩阵挑第一个「Player 层碰得到」的地面层，不猜引擎的层表
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
