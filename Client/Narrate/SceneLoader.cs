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

    static IEnumerator OpenRoutine(string traderId, ClientDialogController dc)
    {
        _busy = true;
        var name = RoomScene(EnsureNarrateBundles(traderId));
        if (name == null) Plugin.Log.LogWarning("[scene] no room bundle/scene for " + traderId);
        else
        {
            if (!SceneManager.GetSceneByName(name).isLoaded)
            {
                var op = SceneManager.LoadSceneAsync(name, LoadSceneMode.Additive);
                while (!op.isDone) yield return null;
            }
            var scene = SceneManager.GetSceneByName(name);
            if (!Requested)
            {
                if (scene.isLoaded)
                {
                    var un = SceneManager.UnloadSceneAsync(scene);
                    while (un != null && !un.isDone) yield return null;
                }
                Plugin.Log.LogDebug("[scene] '" + name + "' discarded - dialog closed during load");
                _busy = false;
                yield break;
            }
            var roots = scene.GetRootGameObjects();
            foreach (var root in roots)
            {
                SceneShaders.Fix(root);
                root.transform.position += new Vector3(0f, 300f, 0f);
            }
            SceneManager.SetActiveScene(scene);
            AudioRouting.Remap(scene);
            SceneRelight.Promote(scene);
            _sceneName = name;
            var cam = CameraPoint(roots);
            if (cam != null) SceneCamera.Show(cam);
            else Plugin.Log.LogWarning("[scene] no camera point in " + name);
            Animate(roots, dc);
            Plugin.Log.LogDebug("[scene] '" + name + "' staged for " + traderId);
        }
        _busy = false;
    }

    static IEnumerator CloseRoutine()
    {
        _busy = true;
        var scene = SceneManager.GetSceneByName(_sceneName);
        _sceneName = null;
        if (scene.isLoaded)
        {
            var op = SceneManager.UnloadSceneAsync(scene);
            while (op != null && !op.isDone) yield return null;
        }
        SceneCamera.Hide();
        _busy = false;
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
