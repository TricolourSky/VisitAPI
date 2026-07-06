using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace VisitAPI.Scene
{
    // The retail-1.0 vendor scene assets. TWO on-disk layouts, detected per root:
    //   RawPack — the self-made 1.0.6 extraction: a flat folder of `vendors_<name>.bundle` scene bundles
    //     (all 8 vendors incl. Peacekeeper) + `vendors_shared.bundle` (load FIRST — every scene depends on
    //     it) + dialogue.json. Raw scenes carry no helper component; VendorSceneSource discovers the
    //     camera/trader by the retail naming convention.
    //   Bmpq — bmpq/spt-tradermod's repack: a root holding tradermod.shared.dll + bundles/vendors/
    //     {<traderId>_*.bundle, vendors_shared, dialogue.json}. His scene bundles serialize a TraderScene
    //     component against the tradermod.shared assembly, so that DLL must be loaded into the game
    //     process before any scene loads — everything on it is then read via reflection.
    internal enum SceneAssetsLayout { None, RawPack, Bmpq }

    internal static class SceneAssets
    {
        internal static string Root = "";
        internal static SceneAssetsLayout Layout { get; private set; }
        internal static string VendorsDir => Layout == SceneAssetsLayout.Bmpq ? Path.Combine(Root, "bundles", "vendors") : Root;
        internal static string SharedBundleFile => Layout == SceneAssetsLayout.Bmpq ? "vendors_shared" : "vendors_shared.bundle";

        // The 8 retail vendors: trader id → the raw pack's bundle name (bmpq's files are named by id instead).
        private static readonly Dictionary<string, string> VendorNamesById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["54cb50c76803fa8b248b4571"] = "prapor",
            ["54cb57776803fa99248b456e"] = "therapist",
            ["579dc571d53a0658a154fbec"] = "fence",
            ["58330581ace78e27b8b10cee"] = "skier",
            ["5935c25fb3acc3127c3d8cd9"] = "peacekeeper",
            ["5a7c2eca46aef81a7ca2145d"] = "mechanic",
            ["5ac3b934156ae10c4430e83c"] = "ragman",
            ["5c0647fdd443bc2504c2d371"] = "jaeger",
        };

        private static readonly Dictionary<string, AssetBundle> _bundles = new Dictionary<string, AssetBundle>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, Shader>? _nativeShaders;

        internal static bool Ready { get; private set; }
        internal static Type? TraderSceneType;
        internal static Type? DialogTypeEnum;
        private static PropertyInfo? _propCameraPoint;
        private static PropertyInfo? _propTraderGameObject;
        private static PropertyInfo? _propDirector;
        private static PropertyInfo? _propTimelineDialogs;
        private static PropertyInfo? _propDialogs;

        // Probe order (config always wins): the shipped scene pack lives at plugins\VisitAPI\scenes\
        // (bmpq/tarkin layout — tradermod.shared.dll + bundles\vendors, NEVER his tradermod.eft.dll which
        // would double-run vendor scenes); plugins\VisitAPI\vendors\ is reserved for a future self-made
        // raw pack. Devs point Scene.AssetsRoot at their working pack via config.
        internal static bool Resolve(string configured)
        {
            foreach (string candidate in new[]
            {
                configured,
                Path.Combine(BepInEx.Paths.PluginPath, "VisitAPI", "scenes"),
                Path.Combine(BepInEx.Paths.PluginPath, "VisitAPI", "vendors"),
            })
            {
                if (string.IsNullOrEmpty(candidate)) continue;
                if (File.Exists(Path.Combine(candidate, "vendors_shared.bundle")))
                {
                    Root = candidate;
                    Layout = SceneAssetsLayout.RawPack;
                    Plugin.Log.LogInfo("[SceneAssets] assets root: " + Root + " (raw 1.0.6 pack)");
                    return true;
                }
                if (File.Exists(Path.Combine(candidate, "tradermod.shared.dll"))
                    && Directory.Exists(Path.Combine(candidate, "bundles", "vendors")))
                {
                    Root = candidate;
                    Layout = SceneAssetsLayout.Bmpq;
                    Plugin.Log.LogInfo("[SceneAssets] assets root: " + Root + " (bmpq layout)");
                    return true;
                }
            }
            Plugin.Log.LogWarning("[SceneAssets] no vendor assets found (need a folder with vendors_shared.bundle [raw pack], or tradermod.shared.dll + bundles\\vendors [bmpq]). Set Scene.AssetsRoot in the config.");
            return false;
        }

        // Load tradermod.shared.dll (Unity binds the scenes' TraderScene component to that exact assembly
        // name) and resolve its members. One-time; PASS/FAIL self-test in the log. The raw pack has no
        // helper assembly — TraderSceneType stays null and VendorSceneSource's name-convention discovery
        // does all the work.
        internal static bool Bind()
        {
            if (Layout == SceneAssetsLayout.RawPack) return true;
            if (Ready) return true;
            if (Root.Length == 0) return false;
            try
            {
                Assembly shared = Assembly.LoadFrom(Path.Combine(Root, "tradermod.shared.dll"));
                TraderSceneType = shared.GetType("tarkin.tradermod.shared.TraderScene");
                DialogTypeEnum = shared.GetType("tarkin.tradermod.shared.ETraderDialogType");
                const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                _propCameraPoint = TraderSceneType?.GetProperty("CameraPoint", All);
                _propTraderGameObject = TraderSceneType?.GetProperty("TraderGameObject", All);
                _propDirector = TraderSceneType?.GetProperty("Director", All);
                _propTimelineDialogs = TraderSceneType?.GetProperty("TimelineDialogs", All);
                _propDialogs = TraderSceneType?.GetProperty("Dialogs", All);

                void Line(string n, bool ok) => Plugin.Log.LogInfo($"[SceneAssets] {(ok ? "PASS" : "FAIL")}  {n}");
                Line("tarkin.tradermod.shared.TraderScene", TraderSceneType != null);
                Line("ETraderDialogType", DialogTypeEnum != null);
                Line("TraderScene.CameraPoint", _propCameraPoint != null);
                Line("TraderScene.TraderGameObject", _propTraderGameObject != null);
                Line("TraderScene.Director", _propDirector != null);
                Line("TraderScene.TimelineDialogs", _propTimelineDialogs != null);
                Line("TraderScene.Dialogs", _propDialogs != null);

                Ready = TraderSceneType != null && _propCameraPoint != null;
                return Ready;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[SceneAssets] load tradermod.shared.dll: " + ex.Message);
                return false;
            }
        }

        // The raw pack's bundles carry AssetRipper dummy shaders under the ORIGINAL shader names.
        // Once one is loaded, Shader.Find(name) may return the dummy instead of the game's real
        // shader, so the name→shader table must be snapshotted from what the game itself has loaded
        // BEFORE the first pack bundle comes in (the menu/hideout has the environment set loaded).
        private static void EnsureShaderSnapshot()
        {
            if (_nativeShaders != null) return;
            _nativeShaders = new Dictionary<string, Shader>(StringComparer.Ordinal);
            foreach (Shader shader in Resources.FindObjectsOfTypeAll<Shader>())
            {
                if (shader == null || string.IsNullOrEmpty(shader.name)) continue;
                if (!_nativeShaders.ContainsKey(shader.name)) _nativeShaders.Add(shader.name, shader);
            }
            // Character hair/cloth shaders aren't loaded at the menu, so the enumeration above misses
            // them; if a raid this session already made them resident, Shader.Find catches the REAL
            // shader. Safe here because this runs BEFORE any pack bundle loads → never an AR dummy.
            foreach (string name in new[] { "Characters/TraiderHair", "Cloth/ClothShader", "Cloth/ClothShader_backface" })
            {
                if (_nativeShaders.ContainsKey(name)) continue;
                Shader found = Shader.Find(name);
                if (found != null) _nativeShaders.Add(name, found);
            }
            Plugin.Log.LogInfo("[SceneAssets] native shader snapshot: " + _nativeShaders.Count + " shader(s)");
        }

        internal static Shader? FindNativeShader(string name)
        {
            if (_nativeShaders == null) return Shader.Find(name);
            return _nativeShaders.TryGetValue(name, out Shader shader) && shader != null ? shader : null;
        }

        internal static string? TraderIdForVendorScene(string sceneName)
        {
            if (!sceneName.StartsWith("Vendors_", StringComparison.OrdinalIgnoreCase)) return null;
            string vendor = sceneName.Substring("Vendors_".Length);
            foreach (KeyValuePair<string, string> kv in VendorNamesById)
                if (string.Equals(kv.Value, vendor, StringComparison.OrdinalIgnoreCase)) return kv.Key;
            return null;
        }

        internal static AssetBundle? GetBundle(string fileName)
        {
            EnsureShaderSnapshot();
            if (_bundles.TryGetValue(fileName, out AssetBundle cached) && cached != null) return cached;
            string path = Path.Combine(VendorsDir, fileName);
            if (!File.Exists(path))
            {
                Plugin.Log.LogWarning("[SceneAssets] bundle missing: " + path);
                return null;
            }
            AssetBundle bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null)
            {
                Plugin.Log.LogWarning("[SceneAssets] bundle failed to load: " + fileName);
                return null;
            }
            _bundles[fileName] = bundle;
            return bundle;
        }

        internal static string? FindVendorBundleFile(string traderId)
        {
            if (string.IsNullOrEmpty(traderId) || !Directory.Exists(VendorsDir)) return null;
            if (Layout == SceneAssetsLayout.RawPack)
            {
                if (!VendorNamesById.TryGetValue(traderId, out string name)) return null;
                string file = "vendors_" + name + ".bundle";
                return File.Exists(Path.Combine(VendorsDir, file)) ? file : null;
            }
            return Directory.GetFiles(VendorsDir, traderId + "*")
                .Select(Path.GetFileName)
                .FirstOrDefault();
        }

        // Bundle by absolute path (the config/VisitAPI/scenes/ drop-in dir) — same cache, keyed by path.
        internal static AssetBundle? GetBundleAtPath(string path)
        {
            EnsureShaderSnapshot();
            if (_bundles.TryGetValue(path, out AssetBundle cached) && cached != null) return cached;
            if (!File.Exists(path))
            {
                Plugin.Log.LogWarning("[SceneAssets] bundle missing: " + path);
                return null;
            }
            AssetBundle bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null)
            {
                Plugin.Log.LogWarning("[SceneAssets] bundle failed to load: " + path);
                return null;
            }
            _bundles[path] = bundle;
            return bundle;
        }

        internal static Transform? GetCameraPoint(Component scene) => _propCameraPoint?.GetValue(scene) as Transform;
        internal static Animator? GetTraderAnimator(Component scene) => _propTraderGameObject?.GetValue(scene) as Animator;
        internal static UnityEngine.Playables.PlayableDirector? GetDirector(Component scene) => _propDirector?.GetValue(scene) as UnityEngine.Playables.PlayableDirector;
        internal static System.Collections.IDictionary? GetTimelineDialogs(Component scene) => _propTimelineDialogs?.GetValue(scene) as System.Collections.IDictionary;
        internal static System.Collections.IDictionary? GetDialogs(Component scene) => _propDialogs?.GetValue(scene) as System.Collections.IDictionary;
    }
}
