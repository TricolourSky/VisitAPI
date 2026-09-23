using System.Collections.Generic;
using EFT;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VisitAPI.Native;

[HarmonyPatch(typeof(NarrateGame), "Move")]
public static class NarrateSpawnGuard
{
    const float EyeHeight = 1.5f;

    public const float Fov11 = 55f;

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
        sceneInfo.playerPosition = cam.position - Vector3.up * EyeHeight;
        sceneInfo.targetPosition = cam.position + cam.forward * 5f;
        _markerScenes.Add(_current);
        CollisionAligner.Align(scene, sceneInfo.playerPosition);
        Plugin.Log.LogInfo(native
            ? $"[narrate] 0.16 原生坐标 {nativePlayer} 脚下没有房间（1.1 把这间房挪了位置），改按 1.1 机位标记定位: {cam.position} 朝向 {cam.forward}"
            : $"[narrate] 按 1.1 机位标记定位: {cam.position} 朝向 {cam.forward}");
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
