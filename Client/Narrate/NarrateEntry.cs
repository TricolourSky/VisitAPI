using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using StringComparer = System.StringComparer;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.Communications;
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
        return true;
    }

    // 09-24 审查低项：从点「访问」到引擎建好 NarrateGame 之间（加载场景、建世界要好几秒）GameExist 还是 false，以前这段时间里能再点一次，
    // 两个 Show 同时跑。Show 结束就清掉；万一协程没走到 finally，5 分钟后也不再拦
    static bool _starting;
    static float _startingAt;

    public static void Visit(string traderId)
    {
        if (!TarkovApplication.Exist(out var app) || app.NarrateControllerAccess == null)
        { Plugin.Log.LogWarning("[narrate] application not ready"); return; }
        if (app.NarrateControllerAccess.GameExist) { Plugin.Log.LogWarning("[narrate] Already in a visit, ignoring repeated Visit"); return; }
        if (_starting && Time.unscaledTime - _startingAt < 300f) { Plugin.Log.LogWarning("[narrate] A visit is already starting, ignoring repeated Visit"); return; }
        if (NarrateHideGuard.Unloading) { Queue(traderId); return; }
        EnsureTraderTypeMap();
        if (!TypeOf(traderId, out var type)) return;
        if (Profile.TraderInfo.TraderIdToType.TryGetValue(traderId, out var engineType) && engineType != type)
            Plugin.Log.LogWarning($"[narrate] Engine table maps {traderId} to {engineType}, using {type} from the 1.1 trader table (SPT moddedTraders override)");
        var bundle = SceneLoader.EnsureNarrateBundles(traderId);
        if (bundle == null || !RegisterScene(traderId, type))
        {
            Plugin.Log.LogWarning("[narrate] scene bundles missing for " + traderId);
            Plugin.Instance.StartCoroutine(Fallback(traderId));
            return;
        }
        SetCommonScene(SceneLoader.TraderScriptsScene(bundle) ?? CommonScene);
        WhitelistPatch.RegisteredTraders.Add(traderId);
        TabRouter.WatchNarrate(app, traderId);
        AmbientGuard.Clear();
        NarrateLoading.Arm(traderId);
        _starting = true;
        _startingAt = Time.unscaledTime;
        Plugin.Instance.StartCoroutine(Run(app, type, traderId));
    }

    // 09-25 SORA 实机（聊天里两封邀请，用掉一封后另一位商人点了没反应）：上一场访问的房间要等商人告别动画（最多 8 秒）才卸载，
    // 这期间点的访问以前直接丢掉。现在记下最后一次点的商人，卸载完自动开始；等太久（20 秒）就放弃
    static string _queued;

    static void Queue(string traderId)
    {
        var first = _queued == null;
        _queued = traderId;
        if (first) Plugin.Instance.StartCoroutine(RunQueued());
    }

    static IEnumerator RunQueued()
    {
        var t0 = Time.unscaledTime;
        while (NarrateHideGuard.Unloading && Time.unscaledTime - t0 < 20f) yield return null;
        var trader = _queued;
        _queued = null;
        if (trader == null) yield break;
        if (NarrateHideGuard.Unloading) { Plugin.Log.LogWarning($"[narrate] room still unloading after 20 s, queued visit to {trader} dropped"); yield break; }
        yield return null;
        Visit(trader);
    }

    /// <summary>1.3.4 B1：原生 Show 建访问世界时传 loadBundlesAndCreatePools: false，假定角色的模型包已经在内存里。
    /// 藏身处建玩家前会先调原生 HideoutPlayer.UpdateHideoutBundles(profile, JobYieldPriority.Low) 把全身服装、装备的包载进对象池，访问流程不调——
    /// 自定义服装（ots14 等）的包第一次访问时还没加载，建玩家就抛「bundle is not loaded」。这里照藏身处的调用原样补上这一步。
    /// 它只是个按档案加载模型包的工具函数，不建藏身处、不碰世界。</summary>
    static IEnumerator PreloadOutfit(TarkovApplication app)
    {
        var profile = app.Session?.Profile;
        if (profile == null) yield break;
        Task task = null;
        try { task = HideoutPlayer.UpdateHideoutBundles(profile, Diz.Jobs.JobYieldPriority.Low); }
        catch (System.Exception e) { Plugin.Log.LogWarning("[narrate] outfit bundle preload failed to start: " + e.Message); }
        while (task != null && !task.IsCompleted) yield return null;
        if (task == null) yield break;
        if (task.IsFaulted) Plugin.Log.LogWarning("[narrate] outfit bundle preload failed (visit continues): " + task.Exception?.GetBaseException()?.Message);
    }

    /// <summary>1.3.4 B1：访问建到一半失败时（原生错误框已被 NarrateShowError 拦下），回到主菜单后给一行提示，
    /// 再用原生 ShowTraderDialogScreen 打开这个商人的 2D 对话，剧情照样能往下走。</summary>
    static IEnumerator Fallback(string traderId)
    {
        yield return UiWait.Until(() => Object.FindObjectOfType<MenuScreen>() != null, 30);
        yield return null;
        try
        {
            NotificationManager.DisplayMessageNotification(Loc.Pick("商人房间没能打开，改用普通对话。", "Could not open the trader's room, using the regular dialog instead."));
            if (!TarkovApplication.Exist(out var app) || !(MenuOpField?.GetValue(app) is MainMenuShowOperation op) || op.DialogController == null)
            { Plugin.Log.LogWarning("[narrate] fallback dialog: main menu not ready"); yield break; }
            if (app.NarrateControllerAccess != null && app.NarrateControllerAccess.GameExist) { Plugin.Log.LogWarning("[narrate] fallback dialog: a visit is running again, skipped"); yield break; }
            if (DialogScreenTracker.Open) { Plugin.Log.LogWarning("[narrate] fallback dialog: a dialog screen is already open"); yield break; }
            WhitelistPatch.RegisteredTraders.Add(traderId);
            var presenter = new InvitePresenter(op.DialogController, traderId);
            op.ShowTraderDialogScreen(new MongoID(traderId), null, presenter);
            Plugin.Instance.StartCoroutine(presenter.Watch());
        }
        catch (System.Exception e) { Plugin.Log.LogError("[narrate] fallback dialog failed: " + e); }
    }

    /// 09-24 审查：Show 之后的每一步都先确认还是同一场访问（同一个 NarrateGame）。以前等对话屏最多 180 秒，
    /// 期间这场访问结束、又开了新的一场，旧协程会去 Abort 新的访问，或者对新访问再 Mute 一次灯（第一批灯就恢复不了）
    static IEnumerator Run(TarkovApplication app, Profile.ETraderType type, string traderId)
    {
        var nc = app.NarrateControllerAccess;
        try
        {
            SubtitleReplay.Arm();
            yield return PreloadOutfit(app);
            var env = EnvironmentManager.Instance == null ? new GameObject("VisitNarrateEnv").AddComponent<EnvironmentManager>() : null;
            TexturePin.Apply();
            NarrateShowError.Last = null;
            Task task = nc.Show(type);
            while (!task.IsCompleted) yield return null;
            _starting = false;
            var failed = NarrateShowError.Last;
            NarrateShowError.Last = null;
            var game = nc._game;
            var world = Narrating.Now && Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
            if (env != null && world != null) env.transform.SetParent(world.transform, worldPositionStays: false);
            else if (env != null) Object.Destroy(env.gameObject);
            if (task.IsFaulted)
            {
                Plugin.Log.LogWarning("[narrate] show failed: " + task.Exception?.GetBaseException()?.Message);
                Abort(app, failedShow: true);
                Plugin.Instance.StartCoroutine(Fallback(traderId));
                yield break;
            }
            if (!Narrating.Now || game == null)
            {
                Plugin.Log.LogWarning("[narrate] visit ended during Show - not staging");
                Abort(app, failedShow: true);
                if (failed != null) Plugin.Instance.StartCoroutine(Fallback(traderId));
                yield break;
            }
            // 引擎 Show 的 try 里 game.Run 失败走 HandleError（1.3.4 起被 NarrateShowError 拦下），游戏停在 Running、对话屏永远不会开
            if (game.Status != GameStatus.Started)
            {
                Plugin.Log.LogWarning($"[narrate] visit game did not start (status {game.Status}), aborting this visit");
                Abort(app);
                if (failed != null) Plugin.Instance.StartCoroutine(Fallback(traderId));
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
            for (var k = 0; k < 30; k++)
            {
                if (nc._game != game) yield break;
                SceneLighting.Uncover(visiting: true);
                yield return null;
            }
            yield return UiWait.Until(() => DialogScreenTracker.Open || nc._game != game, 180);
            if (nc._game != game) yield break;
            if (!DialogScreenTracker.Open)
            {
                Plugin.Log.LogWarning("[narrate] dialog screen never opened - aborting visit");
                Abort(app);
                yield break;
            }
            ForeignLights.Mute();
        }
        finally { _starting = false; }
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

    /// 这个场景是不是访问加载的（公共脚本场景或某个商人房间）——访问收尾只动这些场景里的东西，藏身处自己的不碰
    internal static bool IsVisitScene(Scene scene)
    {
        if (!scene.IsValid() || string.IsNullOrEmpty(scene.name)) return false;
        var name = scene.name;
        if (string.Equals(name, _common, System.StringComparison.OrdinalIgnoreCase)) return true;
        var presets = TarkovApplication.NarrateController.Scenes;
        foreach (var c in presets.commonScenes)
            if (c != null && string.Equals(c.sceneName, name, System.StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var info in presets.narrateScenes.Values)
            if (info != null && string.Equals(info.sceneName, name, System.StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    static void SetCommonScene(string name)
    {
        var list = TarkovApplication.NarrateController.Scenes.commonScenes;
        if (list.Count == 0) list.Add(new NarrateScene { sceneName = name });
        else list[0].sceneName = name;
        _common = name;
    }

    /// failedShow：引擎 Show 刚失败或没建成游戏（Run 里用）。调试键中止时不传，按有没有半截的访问世界 / 访问场景来判断
    public static void Abort(TarkovApplication app = null, bool failedShow = false)
    {
        if (app == null && !TarkovApplication.Exist(out app)) { Plugin.Log.LogWarning("[narrate] abort: no application"); LocalCleanup(); return; }
        var nc = app.NarrateControllerAccess;
        if (nc == null) { Plugin.Log.LogWarning("[narrate] abort: no narrate controller"); LocalCleanup(); return; }
        if (!nc.GameExist)
        {
            if (failedShow || nc._gameWorld != null || AnyVisitSceneLoaded())
            {
                Plugin.Log.LogWarning("[narrate] abort: visit was not built (Show failed midway) - cleaning up the half-built visit world / scenes and returning to main menu");
                NarrateHideGuard.CleanupFailedShow(nc);
                LocalCleanup();
            }
            else { Plugin.Log.LogWarning("[narrate] abort: no active visit - restoring local state only"); LocalCleanup(); }
            return;
        }
        nc.Hide();
    }

    static bool AnyVisitSceneLoaded()
    {
        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            var s = SceneManager.GetSceneAt(i);
            // 主菜单 .dlg 用 SceneLoader 开的房间场景和访问房间同名，不算半截的访问
            if (s.isLoaded && IsVisitScene(s) && !string.Equals(s.name, SceneLoader.OpenScene, System.StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
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
