using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using StringComparer = System.StringComparer;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.EnvironmentEffect;
using EFT.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VisitAPI.Native;

public static class NarrateEntry
{
    const string CommonScene = "Vendors_Scripts";
    static bool _mapped;
    static string _common = CommonScene;
    internal static readonly FieldInfo MenuOpField = AccessTools.Field(typeof(TarkovApplication), "_menuOperation");

    static readonly Dictionary<string, Profile.ETraderType> KnownTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["54cb50c76803fa8b248b4571"] = Profile.ETraderType.Prapor,
        ["54cb57776803fa99248b456e"] = Profile.ETraderType.Terapevt,
        ["579dc571d53a0658a154fbec"] = Profile.ETraderType.Fence,
        ["58330581ace78e27b8b10cee"] = Profile.ETraderType.Skier,
        ["5935c25fb3acc3127c3d8cd9"] = Profile.ETraderType.Peacekeeper,
        ["5a7c2eca46aef81a7ca2145d"] = Profile.ETraderType.Mechanic,
        ["5ac3b934156ae10c4430e83c"] = Profile.ETraderType.Ragman,
        ["5c0647fdd443bc2504c2d371"] = (Profile.ETraderType)12,
    };

    static bool TypeOf(string traderId, out Profile.ETraderType type)
    {
        if (KnownTypes.TryGetValue(traderId, out type)) return true;
        return Profile.TraderInfo.TraderIdToType.TryGetValue(traderId, out type);
    }

    public static bool CanVisit(string traderId)
    {
        if (!SceneLoader.HasRoom(traderId)) return false;
        EnsureTraderTypeMap();
        if (!TypeOf(traderId, out _)) return false;
        var cfg = Singleton<GlobalConfiguration>.Instance;
        return cfg != null && cfg.TradersSettings.TryGetValue(traderId, out var s) && s != null && s.MainDialog.HasValue;
    }

    static bool RegisterScene(string traderId, Profile.ETraderType type)
    {
        if (TarkovApplication.NarrateController.Scenes.IsValid(type, out _)) return true;
        var bundle = SceneLoader.EnsureNarrateBundles(traderId);
        var sceneName = SceneLoader.RoomScene(bundle);
        if (sceneName == null) return false;
        TarkovApplication.NarrateController.Scenes.narrateScenes[type] = new NarrateSceneInfo { sceneName = sceneName };
        Plugin.Log.LogInfo($"[narrate] scene preset registered: {type} -> {sceneName}");
        return true;
    }

    public static void Visit(string traderId)
    {
        if (!TarkovApplication.Exist(out var app) || app.NarrateControllerAccess == null)
        { Plugin.Log.LogWarning("[narrate] application not ready"); return; }
        if (app.NarrateControllerAccess.GameExist) { Plugin.Log.LogWarning("[narrate] 已在访问中，忽略再次 Visit"); return; }
        EnsureTraderTypeMap();
        if (!TypeOf(traderId, out var type)) return;
        if (Profile.TraderInfo.TraderIdToType.TryGetValue(traderId, out var engineType) && engineType != type)
            Plugin.Log.LogWarning($"[narrate] 引擎表把 {traderId} 记成 {engineType}，按 1.1 商人表用 {type}（SPT 的 moddedTraders 覆写）");
        var bundle = SceneLoader.EnsureNarrateBundles(traderId);
        if (bundle == null || !RegisterScene(traderId, type))
        { Plugin.Log.LogWarning("[narrate] scene bundles missing for " + traderId); return; }
        SetCommonScene(SceneLoader.TraderScriptsScene(bundle) ?? CommonScene);
        WhitelistPatch.RegisteredTraders.Add(traderId);
        TabRouter.WatchNarrate(app, traderId);
        AmbientGuard.Clear();
        NarrateLoading.Arm(traderId);
        Plugin.Instance.StartCoroutine(Run(app, type));
    }

    static IEnumerator Run(TarkovApplication app, Profile.ETraderType type)
    {
        var env = EnvironmentManager.Instance == null ? new GameObject("VisitNarrateEnv").AddComponent<EnvironmentManager>() : null;
        TexturePin.Apply();
        Task task = app.NarrateControllerAccess.Show(type);
        while (!task.IsCompleted) yield return null;
        var world = Narrating.Now && Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        if (env != null && world != null) env.transform.SetParent(world.transform, worldPositionStays: false);
        else if (env != null) Object.Destroy(env.gameObject);
        if (task.IsFaulted)
        {
            Plugin.Log.LogWarning("[narrate] show failed: " + task.Exception?.GetBaseException()?.Message);
            Abort(app);
            yield break;
        }
        if (!Narrating.Now)
        {
            Plugin.Log.LogWarning("[narrate] visit ended during Show - not staging");
            Abort(app);
            yield break;
        }
        if (!TarkovApplication.NarrateController.Scenes.IsValid(type, out var info))
        {
            Plugin.Log.LogWarning("[narrate] scene preset lost for " + type);
            Abort(app);
            yield break;
        }
        var scene = SceneManager.GetSceneByName(info.sceneName);
        var common = SceneManager.GetSceneByName(_common);
        if (!common.isLoaded) Plugin.Log.LogWarning($"[narrate] common scene '{_common}' not loaded - lighting/decals will be off");
        Stage(scene, common);
        for (var k = 0; k < 30; k++) { SceneLighting.Uncover(visiting: true); yield return null; }
        yield return UiWait.Until(() => DialogScreenTracker.Open, 180);
        if (!DialogScreenTracker.Open)
        {
            Plugin.Log.LogWarning("[narrate] dialog screen never opened - aborting visit (bind Debug.AbortVisitKey in the config if it stays stuck)");
            Abort(app);
            yield break;
        }
        var cm = EFT.CameraControl.CameraManager.Instance;
        if (cm != null && cm.Camera != null)
            Plugin.Log.LogInfo($"[narrate] steady camera: pos={cm.Camera.transform.position} rot={cm.Camera.transform.eulerAngles} fov={cm.Camera.fieldOfView:0.##}");
        ForeignLights.Mute();
    }

    static void Stage(Scene scene, Scene common)
    {
        Step("lighting", () => SceneLighting.Apply(scene));
        Step("common shaders", () =>
        {
            if (common.isLoaded)
                foreach (var root in common.GetRootGameObjects()) SceneShaders.Fix(root);
            SceneShaders.ReportMisses();
        });
        Step("ambient snapshot", AmbientDrawGuard.RebuildSnapshot);
        foreach (var s in new[] { scene, common })
        {
            Step("season", () => SeasonGate.Apply(s));
            Step("audio remap", () => AudioRouting.Remap(s));
            Step("ambient audio", () => AudioRouting.EnsureAmbient(s));
            Step("relight", () => SceneRelight.Promote(s));
        }
        Step("glass", () => GlassProbe.Report(scene));
    }

    static void Step(string name, System.Action act)
    {
        try { act(); }
        catch (System.Exception e) { Plugin.Log.LogError($"[narrate] stage '{name}' failed (continuing): {e}"); }
    }

    static void SetCommonScene(string name)
    {
        var list = TarkovApplication.NarrateController.Scenes.commonScenes;
        if (list.Count == 0) list.Add(new NarrateScene { sceneName = name });
        else list[0].sceneName = name;
        _common = name;
        Plugin.Log.LogInfo($"[narrate] common scene for this visit: {name}");
    }

    public static void Abort(TarkovApplication app = null)
    {
        if (app == null && !TarkovApplication.Exist(out app)) { Plugin.Log.LogWarning("[narrate] abort: no application"); LocalCleanup(); return; }
        if (app.NarrateControllerAccess == null) { Plugin.Log.LogWarning("[narrate] abort: no narrate controller"); LocalCleanup(); return; }
        if (!app.NarrateControllerAccess.GameExist) { Plugin.Log.LogWarning("[narrate] abort: no active visit - restoring local state only"); LocalCleanup(); return; }
        app.NarrateControllerAccess.Hide();
        Plugin.Log.LogInfo("[narrate] visit aborted - back to menu");
    }

    static void LocalCleanup()
    {
        try
        {
            ForeignLights.Restore();
            TexturePin.Restore();
            NarrateLoading.ForceClose();
            TabRouter.UnwatchNarrate();
            SceneLighting.Release();
            Plugin.Log.LogInfo("[narrate] local state restored after aborted visit");
        }
        catch (System.Exception e) { Plugin.Log.LogError("[narrate] local cleanup failed: " + e); }
    }

    public static void EnsureMenu()
    {
        SceneCamera.Hide();
        SceneLighting.Release();
        if (TarkovApplication.Exist(out var app) && MenuOpField?.GetValue(app) is MainMenuShowOperation op)
            Plugin.Instance.StartCoroutine(MenuWatch(op));
    }

    static IEnumerator MenuWatch(MainMenuShowOperation op)
    {
        yield return UiWait.Until(() => Object.FindObjectOfType<MenuScreen>() != null, 90);
        if (Object.FindObjectOfType<MenuScreen>() != null) yield break;
        Plugin.Log.LogWarning("[narrate] main menu did not return - forcing it");
        op.ShowMenuScreenSync();
    }

    static void EnsureTraderTypeMap()
    {
        if (_mapped) return;
        _mapped = true;
        var fwd = (Dictionary<MongoID, Profile.ETraderType>)Profile.TraderInfo.TraderIdToType;
        var rev = (Dictionary<Profile.ETraderType, MongoID>)Profile.TraderInfo.TraderTypeToId;
        Map(fwd, rev, "5a7c2eca46aef81a7ca2145d", Profile.ETraderType.Mechanic);
        Map(fwd, rev, "5c0647fdd443bc2504c2d371", (Profile.ETraderType)12);
    }

    static void Map(Dictionary<MongoID, Profile.ETraderType> fwd, Dictionary<Profile.ETraderType, MongoID> rev, MongoID id, Profile.ETraderType type)
    {
        if (!fwd.ContainsKey(id)) fwd.Add(id, type);
        if (!rev.ContainsKey(type)) rev.Add(type, id);
    }
}
