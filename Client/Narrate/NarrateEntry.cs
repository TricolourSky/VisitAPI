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
    // _menuOperation 是 private，没在公开化范围里，走 Harmony 反射取
    static readonly FieldInfo MenuOpField = AccessTools.Field(typeof(TarkovApplication), "_menuOperation");

    /// 主菜单的那套操作对象（对话控制器、任务 / 背包控制器都挂在它上面）；应用没建好或还没进主菜单就 false
    internal static bool TryMenuOp(out TarkovApplication app, out MainMenuShowOperation op)
    {
        op = null;
        if (!TarkovApplication.Exist(out app)) return false;
        op = MenuOpField?.GetValue(app) as MainMenuShowOperation;
        return op != null;
    }

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

    static bool TypeOf(string traderId, out Profile.ETraderType type) =>
        KnownTypes.TryGetValue(traderId, out type) || Profile.TraderInfo.TraderIdToType.TryGetValue(traderId, out type);

    public static bool CanVisit(string traderId)
    {
        if (!SceneLoader.HasRoom(traderId)) return false;
        EnsureTraderTypeMap();
        if (!TypeOf(traderId, out _)) return false;
        var cfg = Singleton<GlobalConfiguration>.Instance;
        return cfg != null && cfg.TradersSettings.TryGetValue(traderId, out var s) && s != null && s.MainDialog.HasValue;
    }

    static bool RegisterScene(AssetBundle bundle, Profile.ETraderType type)
    {
        if (TarkovApplication.NarrateController.Scenes.IsValid(type, out _)) return true;
        var sceneName = SceneLoader.RoomScene(bundle);
        if (sceneName == null) return false;
        TarkovApplication.NarrateController.Scenes.narrateScenes[type] = new NarrateSceneInfo { sceneName = sceneName };
        return true;
    }

    // 09-24 审查低项：从点「访问」到引擎建好 NarrateGame 之间（加载场景、建世界要好几秒）GameExist 还是 false，这段时间里再点一次就会
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
        if (bundle == null || !RegisterScene(bundle, type))
        {
            Plugin.Log.LogWarning("[narrate] scene bundles missing for " + traderId);
            Plugin.Instance.StartCoroutine(Fallback(traderId));
            return;
        }
        SetCommonScene(SceneLoader.TraderScriptsScene(bundle) ?? CommonScene);
        WhitelistPatch.RegisteredTraders.Add(traderId);
        TabRouter.WatchNarrate(traderId);
        AmbientGuard.Clear();
        NarrateLoading.Arm(traderId);
        _starting = true;
        _startingAt = Time.unscaledTime;
        Plugin.Instance.StartCoroutine(Run(app, type, traderId));
    }

    // 09-25 SORA 实机（聊天里两封邀请，用掉一封后另一位商人点了没反应）：上一场访问的房间要等商人告别动画（最多 8 秒）才卸载，
    // 这期间点的访问记下最后一次点的商人，卸载完自动开始；等太久（20 秒）就放弃
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
    /// 再用原生 ShowTraderDialogScreen 打开这个商人的 2D 对话，剧情照样能往下走。
    /// 和 InviteDialog.Open 不是一回事，别合：这里用的是主菜单常驻的 op.DialogController + 原生 ShowTraderDialogScreen（让原生流程自己收尾），
    /// 聊天「回复」那边是新建一个 ClientDialogController 开屏；两种用法都是有意的（10-24 审查 D13 定）。</summary>
    /// 10-29：从藏身处发起的访问失败时主菜单不在显示（关掉的屏是 inactive 的，FindObjectOfType 找不到），MenuWatch 要到 90 帧后才把它强制弹回来——
    /// 以前这里只等 30 帧就开对话，随后弹回来的主菜单（Root 屏先关当前屏）把刚开的对话关了。真等到主菜单出现再开，等不到就不开
    static IEnumerator Fallback(string traderId)
    {
        yield return UiWait.Until(() => Object.FindObjectOfType<MenuScreen>() != null, 200);
        if (Object.FindObjectOfType<MenuScreen>() == null) { Plugin.Log.LogWarning("[narrate] fallback dialog: the main menu did not come back within 200 frames, not opening the regular dialog"); yield break; }
        yield return null;
        try
        {
            NotificationManager.DisplayMessageNotification(Loc.Pick("商人房间没能打开，改用普通对话。", "Could not open the trader's room, using the regular dialog instead."));
            if (!TryMenuOp(out var app, out var op) || op.DialogController == null)
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

    /// <summary>09-24 审查：Show 之后的每一步都先确认还是同一场访问（同一个 NarrateGame）。等对话屏最多 180 帧，
    /// 期间这场访问结束、又开了新的一场的话，旧协程会去 Abort 新的访问，或者对新访问再 Mute 一次灯。
    /// 10-24：比较用 ReferenceEquals——Unity 重载的 `!=` 把「已销毁的 game」和「变成 null 的 nc._game」判成相等，访问中途被收掉时协程认不出来</summary>
    static bool SameVisit(TarkovApplication.NarrateController nc, NarrateGame game) => ReferenceEquals(nc._game, game);

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
                if (!SameVisit(nc, game)) yield break;
                SceneLighting.Uncover(visiting: true);
                yield return null;
            }
            yield return UiWait.Until(() => DialogScreenTracker.Open || !SameVisit(nc, game), 180);
            if (!SameVisit(nc, game)) yield break;
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
        Step("glass", () => GlassProbe.Apply(scene));
    }

    static void Step(string name, System.Action act) => VisitSteps.Run("[narrate] stage '{0}' failed (continuing): {1}", name, act);

    /// 这个场景是不是访问加载的（公共脚本场景或某个商人房间）——访问收尾只动这些场景里的东西，藏身处自己的不碰
    internal static bool IsVisitScene(Scene scene)
    {
        if (!scene.IsValid() || string.IsNullOrEmpty(scene.name)) return false;
        var name = scene.name;
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

    /// failedShow：引擎 Show 刚失败或没建成游戏。不传时访问游戏要是已经不在了（等对话屏那次，等待期间访问被别处收掉），
    /// 按有没有半截的访问世界 / 访问场景来判断
    static void Abort(TarkovApplication app, bool failedShow = false)
    {
        var nc = app.NarrateControllerAccess;
        if (!nc.GameExist)
        {
            if (failedShow || nc._gameWorld != null || AnyVisitSceneLoaded())
            {
                Plugin.Log.LogWarning("[narrate] abort: visit was not built (Show failed midway) - cleaning up the half-built visit world / scenes and returning to main menu");
                NarrateHideGuard.CleanupFailedShow(nc);
            }
            else Plugin.Log.LogWarning("[narrate] abort: no active visit - restoring local state only");
            LocalCleanup();
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
        if (TryMenuOp(out _, out var op)) Plugin.Instance.StartCoroutine(MenuWatch(op));
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
        // 按 1.1 的商人表整张补，Map 只补缺的——引擎表里本来就有的那几位（Prapor 等）原样不动
        foreach (var kv in KnownTypes) Map(fwd, rev, kv.Key, kv.Value);
    }

    static void Map(Dictionary<MongoID, Profile.ETraderType> fwd, Dictionary<Profile.ETraderType, MongoID> rev, MongoID id, Profile.ETraderType type)
    {
        if (!fwd.ContainsKey(id)) fwd.Add(id, type);
        if (!rev.ContainsKey(type)) rev.Add(type, id);
    }
}
