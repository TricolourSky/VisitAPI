using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace VisitAPI.Scene
{
    // What the staging needs from ANY vendor scene: where the camera goes and which Animator is the trader
    // model. Discovered from three sources through one probe order:
    //   1. bmpq's TraderScene component on the scene root (his repacked bundles carry it);
    //   2. the retail naming convention (raw 1.0 scene rebuilds) — camera anchor `Position_Camera_<Trader>`,
    //      trader model = the Animator whose GameObject name says vendor/trader/model/holder AND that skins
    //      meshes (display-shelf weapons also have Animators — weapon_/launcher_/mod_/item_*.generated —
    //      but no SkinnedMeshRenderer under them);
    //   3. the author convention for custom scenes (a `Position_Camera` node + an Animator-bearing trader
    //      node, documented in DLG_FORMAT) — same rules as 2.
    internal sealed class VendorScene
    {
        internal Transform? CameraPoint;
        internal Animator? TraderAnimator;
        internal Component? TraderSceneComp;
    }

    internal static class VendorSceneSource
    {
        private static readonly Regex TraderNameRegex = new Regex("vendor|trader|_model|_holder", RegexOptions.IgnoreCase);
        private static readonly Regex PropNameRegex = new Regex("^(weapon_|launcher_|mod_|item_)", RegexOptions.IgnoreCase);

        internal static VendorScene Discover(GameObject[] roots)
        {
            VendorScene scene = new VendorScene();
            if (roots.Length > 0 && SceneAssets.TraderSceneType != null)
            {
                Component? comp = roots[0].GetComponent(SceneAssets.TraderSceneType);
                if (comp != null)
                {
                    scene.TraderSceneComp = comp;
                    scene.CameraPoint = SceneAssets.GetCameraPoint(comp);
                    scene.TraderAnimator = SceneAssets.GetTraderAnimator(comp);
                }
            }
            if (scene.CameraPoint == null) scene.CameraPoint = FindCameraPoint(roots);
            if (scene.TraderAnimator == null) scene.TraderAnimator = FindTraderAnimator(roots);
            Plugin.Log.LogInfo("[VendorScene] source=" + (scene.TraderSceneComp != null ? "TraderScene component" : "name convention")
                + " camera=" + (scene.CameraPoint != null ? scene.CameraPoint.name : "MISSING")
                + " trader=" + (scene.TraderAnimator != null ? scene.TraderAnimator.gameObject.name : "MISSING"));
            return scene;
        }

        private static Transform? FindCameraPoint(GameObject[] roots)
        {
            foreach (GameObject root in roots)
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith("Position_Camera", StringComparison.OrdinalIgnoreCase))
                        return t;
            return null;
        }

        private static Animator? FindTraderAnimator(GameObject[] roots)
        {
            Animator? fallback = null;
            foreach (GameObject root in roots)
            {
                foreach (Animator a in root.GetComponentsInChildren<Animator>(true))
                {
                    string name = a.gameObject.name;
                    if (PropNameRegex.IsMatch(name) || name.Contains(".generated")) continue;
                    if (a.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length == 0) continue;
                    if (TraderNameRegex.IsMatch(name)) return a;
                    fallback ??= a;
                }
            }
            return fallback;
        }
    }
}
