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

/// <summary>
/// 商人房间 bundle 的定位/加载/缓存 + 自定义 `.dlg` `scene:` 指令的第二套场景加载器。
/// 2026-09-05 起全部房间包都出自本项目的 1.1 直打管线，tarkin 的包（vendors_shared / tradermod.shared.dll）已整体剥离：
/// 公共场景 `Vendors_Scripts` 单独成包 `vendors_scripts`，每个房间包只带自己的房间（Jaeger / Peacekeeper / Skier 另带各自专属的
/// `Vendors_X_Scripts`）。相机点按 `Position_Camera*` 命名找。
/// </summary>
public static class SceneLoader
{
    static readonly Dictionary<string, AssetBundle> _bundles = new(StringComparer.OrdinalIgnoreCase);
    static List<string> _roomFiles;      // 房间文件列表按目录 mtime 缓存：CanVisit 每次选商人都要问（旧 N15）
    static DateTime _roomStamp;
    static string _sceneName;
    static bool _busy;

    public static bool IsOpen => _sceneName != null;
    public static bool Requested { get; private set; }

    static string Root => Path.Combine(BepInEx.Paths.PluginPath, "VisitAPI", "scenes");

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
        var name = RoomScene(EnsureNarrateBundles(traderId));   // 包缺失或包里没有场景都是 null（后者：LoadSceneAsync(null) 会返回空操作，协程当场 NRE）
        if (name == null) Plugin.Log.LogWarning("[scene] no room bundle/scene for " + traderId);
        else
        {
            if (!SceneManager.GetSceneByName(name).isLoaded)
            {
                var op = SceneManager.LoadSceneAsync(name, LoadSceneMode.Additive);
                while (!op.isDone) yield return null;
            }
            var scene = SceneManager.GetSceneByName(name);
            if (!Requested)   // 加载途中对话已经关了：丢弃
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
                root.transform.position += new Vector3(0f, 300f, 0f);   // 抬离菜单/藏身处几何避免穿插
            }
            SceneManager.SetActiveScene(scene);
            AudioRouting.Remap(scene);   // 自定义路径的 3D 场景同病同修
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

    /// <summary>
    /// 包里哪个场景才是商人房间。1.1 的结构是「一个商人多个场景」：房间 + 公共 `Vendors_Scripts`。
    /// 拿 `GetAllScenePaths()[0]` 会随机拿到公共那个；判据：名字不以 `_Scripts` 结尾的那个。
    /// </summary>
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

    /// <summary>
    /// 公共场景包 + 房间包都常驻。公共场景必须单独成包：两个房间包各带一份 `Vendors_Scripts` 的话，
    /// 包内文件名 `BuildPlayer-Vendors_Scripts` 撞车，Unity 拒绝加载第二个包（"another AssetBundle with the same files is already loaded"）。
    /// </summary>
    internal static AssetBundle EnsureNarrateBundles(string traderId)
    {
        if (Bundle("vendors_scripts") == null) return null;
        var roomFile = RoomFile(traderId);
        return roomFile != null ? Bundle(roomFile) : null;
    }

    /// <summary>
    /// 房间包里随带的专属公共场景（`Vendors_Jaeger_Scripts` 这类：比公共的多了日光 / 天气 / 大气）。
    /// 1.1 里这三个商人加载的就是自己那份而不是 `Vendors_Scripts`；没有则返回 null，用公共的。
    /// </summary>
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
        var dir = Path.Combine(Root, "bundles", "vendors");
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
        // 快照必须先于任何 bundle 加载, 否则 rip 包残缺 shader 副本会混入快照(旧 DEV_NOTES #57)
        SceneShaders.Snapshot();
        if (_bundles.TryGetValue(file, out var cached) && cached != null) return cached;
        var path = Path.Combine(Root, "bundles", "vendors", file);
        if (!File.Exists(path)) { Plugin.Log.LogWarning("[scene] bundle missing: " + path); return null; }
        // bundle 常驻不卸载是刻意的：场景可能二次进入，卸了再载反而是崩溃温床
        return _bundles[file] = AssetBundle.LoadFromFile(path);
    }
}
