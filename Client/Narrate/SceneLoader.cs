using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EFT;
using EFT.Dialogs;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VisitAPI.Native;

public static class SceneLoader
{
    static readonly Dictionary<string, AssetBundle> _bundles = new(StringComparer.OrdinalIgnoreCase);
    static List<string> _roomFiles;
    static DateTime _roomStamp;
    static string _sceneName;
    static bool _busy;

    public static bool IsOpen => _sceneName != null;
    internal static string OpenScene => _sceneName;
    public static bool Requested { get; private set; }

    /// <summary>房间包目录：rooms\（老的 scenes\bundles\vendors\ 还认，见 VisitPaths）。</summary>
    static string RoomsDir => VisitPaths.Rooms;

    public static void Open(string traderId, ClientDialogController dc)
    {
        if (_busy || IsOpen) return;
        Requested = true;
        Plugin.Instance.StartCoroutine(OpenRoutine(traderId, dc));
    }

    public static void Close()
    {
        Requested = false;
        if (!_busy && IsOpen) Plugin.Instance.StartCoroutine(CloseRoutine());
    }

    /// 09-24 审查 M3：_busy 放进 try/finally。以前布置场景的任一步抛异常，协程中止、_busy 永远是 true，
    /// 之后 Open 直接返回、Close 也不卸场景，这个功能整局失效。布置的各步也单独兜住，一步失败不影响其余
    static IEnumerator OpenRoutine(string traderId, ClientDialogController dc)
    {
        _busy = true;
        try
        {
            var name = RoomScene(EnsureNarrateBundles(traderId));
            if (name == null) { Plugin.Log.LogWarning("[scene] no room bundle/scene for " + traderId); yield break; }
            if (!SceneManager.GetSceneByName(name).isLoaded)
            {
                var op = SceneManager.LoadSceneAsync(name, LoadSceneMode.Additive);
                while (op != null && !op.isDone) yield return null;
            }
            var scene = SceneManager.GetSceneByName(name);
            if (!Requested)
            {
                if (scene.isLoaded)
                {
                    var un = SceneManager.UnloadSceneAsync(scene);
                    while (un != null && !un.isDone) yield return null;
                }
                yield break;
            }
            if (!scene.isLoaded) { Plugin.Log.LogWarning("[scene] '" + name + "' did not load"); yield break; }
            var roots = scene.GetRootGameObjects();
            foreach (var root in roots)
            {
                Step("shaders", () => SceneShaders.Fix(root));
                root.transform.position += new Vector3(0f, 300f, 0f);
            }
            var active = SceneManager.GetActiveScene();
            if (active != scene) { _previousActive = active; SceneManager.SetActiveScene(scene); }
            _sceneName = name;   // 先记下，后面哪步失败 Close 也能把场景卸掉
            Step("audio remap", () => AudioRouting.Remap(scene));
            Step("relight", () => SceneRelight.Promote(scene));
            var cam = CameraPoint(roots);
            if (cam != null) Step("camera", () => SceneCamera.Show(cam));
            else Plugin.Log.LogWarning("[scene] no camera point in " + name);
            Step("animate", () => Animate(roots, dc));
        }
        finally { _busy = false; }
    }

    /// 卸载前把活动场景设回打开前的那个（灯光、天空盒等 RenderSettings 跟着活动场景走）
    static IEnumerator CloseRoutine()
    {
        _busy = true;
        try
        {
            var scene = SceneManager.GetSceneByName(_sceneName);
            _sceneName = null;
            if (_previousActive.IsValid() && _previousActive.isLoaded && SceneManager.GetActiveScene() == scene)
                Step("active scene", () => SceneManager.SetActiveScene(_previousActive));
            _previousActive = default;
            if (scene.isLoaded)
            {
                var op = SceneManager.UnloadSceneAsync(scene);
                while (op != null && !op.isDone) yield return null;
            }
        }
        finally
        {
            Step("camera", SceneCamera.Hide);
            _busy = false;
        }
    }

    static Scene _previousActive;

    static void Step(string name, Action act)
    {
        try { act(); }
        catch (Exception e) { Plugin.Log.LogError($"[scene] Room scene step '{name}' failed (continuing with remaining steps): {e}"); }
    }

    internal static bool HasRoom(string traderId) => RoomFile(traderId) != null;

    internal static string RoomScene(AssetBundle bundle)
    {
        var paths = bundle?.GetAllScenePaths();
        if (paths == null || paths.Length == 0) return null;
        foreach (var p in paths)
        {
            var n = Path.GetFileNameWithoutExtension(p);
            if (!n.EndsWith("_Scripts", StringComparison.OrdinalIgnoreCase)) return n;
        }
        return Path.GetFileNameWithoutExtension(paths[0]);
    }

    internal static AssetBundle EnsureNarrateBundles(string traderId)
    {
        if (Bundle("vendors_scripts") == null) return null;
        var roomFile = RoomFile(traderId);
        return roomFile != null ? Bundle(roomFile) : null;
    }

    internal static string TraderScriptsScene(AssetBundle bundle)
    {
        var paths = bundle?.GetAllScenePaths();
        if (paths == null) return null;
        foreach (var p in paths)
        {
            var n = Path.GetFileNameWithoutExtension(p);
            if (n.EndsWith("_Scripts", StringComparison.OrdinalIgnoreCase)) return n;
        }
        return null;
    }

    static string RoomFile(string traderId)
    {
        var dir = RoomsDir;
        if (!Directory.Exists(dir)) return null;
        var stamp = Directory.GetLastWriteTimeUtc(dir);
        if (_roomFiles == null || stamp != _roomStamp)
        {
            _roomStamp = stamp;
            _roomFiles = Directory.GetFiles(dir).Select(Path.GetFileName).ToList();
        }
        return _roomFiles.FirstOrDefault(f => f.StartsWith(traderId, StringComparison.OrdinalIgnoreCase));
    }

    static void Animate(GameObject[] roots, ClientDialogController dc)
    {
        var animator = roots.SelectMany(r => r.GetComponentsInChildren<Animator>(includeInactive: true))
            .FirstOrDefault(a => a.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true).Length != 0);
        if (animator == null || dc == null)
        {
            Plugin.Log.LogWarning("[scene] no trader model found - lines will play silent");
            return;
        }
        var npc = animator.GetComponent<NPCObject>() ?? animator.gameObject.AddComponent<NPCObject>();
        dc._animationController = new TraderAnimationController(npc, dc);
    }

    internal static Transform FindCameraPoint(Scene scene) =>
        scene.isLoaded ? CameraPoint(scene.GetRootGameObjects()) : null;

    /// 1.1 访问的机位：房间里原生的 StaticCameraObservationPoint（1.1 挂在 Position_Camera_&lt;商人&gt; 上，0.16 有同名同字段的类）。
    /// 组件没解析出来时退回同名物体（就是同一个物体）
    internal static Transform FindObservationPoint(Scene scene)
    {
        if (!scene.isLoaded) return null;
        var roots = scene.GetRootGameObjects();
        foreach (var root in roots)
        {
            var point = root.GetComponentInChildren<StaticCameraObservationPoint>(includeInactive: true);
            if (point != null) return point.transform;
        }
        return CameraPoint(roots);
    }

    static Transform CameraPoint(GameObject[] roots) =>
        roots.SelectMany(r => r.GetComponentsInChildren<Transform>(includeInactive: true))
            .FirstOrDefault(x => x.name.StartsWith("Position_Camera", StringComparison.OrdinalIgnoreCase));

    static AssetBundle Bundle(string file)
    {
        SceneShaders.Snapshot();
        if (_bundles.TryGetValue(file, out var cached) && cached != null) return cached;
        var path = Path.Combine(RoomsDir, file);
        if (!File.Exists(path)) { Plugin.Log.LogWarning("[scene] bundle missing: " + path); return null; }
        return _bundles[file] = AssetBundle.LoadFromFile(path);
    }
}
