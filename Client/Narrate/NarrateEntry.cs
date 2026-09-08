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

/// <summary>
/// 原生访问管线的门面：判定可访问 → 注册场景 → 驱动引擎的 NarrateControllerAccess.Show → 退出兜底。
/// 与 1.2.1 的差别：CanVisit 不再在「选商人」的补丁里同步加载 bundle（旧 N15），
/// 场景注册挪到真正点「访问」的 Visit() 里。
/// </summary>
public static class NarrateEntry
{
    const string CommonScene = "Vendors_Scripts";
    static bool _mapped;
    static string _common = CommonScene;   // 本次访问实际加载的公共场景名（专属 `Vendors_X_Scripts` 或公共 `Vendors_Scripts`）
    internal static readonly FieldInfo MenuOpField = AccessTools.Field(typeof(TarkovApplication), "_menuOperation");

    /// <summary>
    /// 8 个 1.1 商人的枚举映射，**不信引擎表**：SPT 自己的 `TraderCustomizationManager.AddModdedTraders` 会把服务端 `/singleplayer/moddedTraders`
    /// 报上来的每个商人一律改成 `ETraderType.Ragman`（09-06 用 Cecil 从 spt-singleplayer.dll 里拆出来的），D:\EFT 上 Fence 就被它改成了 Ragman——
    /// 于是 Fence 的访问按 Ragman 注册场景、Ragman 再进就进了 Fence 的房。Jaeger 在本 build 枚举里没有命名成员，12 是 1.0 官方枚举表核对出的数值。
    /// </summary>
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

    /// <summary>轻量判定：只看房间文件在不在 + 商人映射 + 引擎有主对话配置。不碰磁盘 bundle。</summary>
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
        // 包里可能有多个场景（房间 + 公共 Vendors_Scripts），要挑房间那个，不能拿 [0]
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
        if (app.NarrateControllerAccess.GameExist) { Plugin.Log.LogWarning("[narrate] 已在访问中，忽略再次 Visit"); return; }   // 公共入口自守（09-07 终审）
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
        AmbientGuard.Clear();   // 双保险：上次退出若有漏网死光源，进场前再清一遍
        NarrateLoading.Arm(traderId);   // 引擎接下来弹的加载屏换成 1.1 的商人加载屏
        Plugin.Instance.StartCoroutine(Run(app, type));
    }

    static IEnumerator Run(TarkovApplication app, Profile.ETraderType type)
    {
        var env = EnvironmentManager.Instance == null ? new GameObject("VisitNarrateEnv").AddComponent<EnvironmentManager>() : null;
        TexturePin.Apply();   // 必须在场景加载前：贴花贴图数组按当时的 mip 上限建（坑 #112）
        Task task = app.NarrateControllerAccess.Show(type);
        while (!task.IsCompleted) yield return null;
        // 不能硬转 NarrateGameWorld：Fika 在场时访问期的世界是它自己的类型（见 Narrating.Now 注释），这里只要一个挂载点
        var world = Narrating.Now && Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        if (env != null && world != null) env.transform.SetParent(world.transform, worldPositionStays: false);
        else if (env != null) Object.Destroy(env.gameObject);
        if (task.IsFaulted)
        {
            Plugin.Log.LogWarning("[narrate] show failed: " + task.Exception?.GetBaseException()?.Message);
            Abort(app);
            yield break;
        }
        // Show 期间被中止（配置的逃生键 / 引擎自己 Hide 了）：别再往下把菜单环境藏一次（09-07 终审）
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
        // 原生 Show 后的若干帧内环境 UI/相机会被反复重隐藏，连续压制到稳定为止
        for (var k = 0; k < 30; k++) { SceneLighting.Uncover(visiting: true); yield return null; }
        yield return UiWait.Until(() => DialogScreenTracker.Open, 180);
        if (!DialogScreenTracker.Open)
        {
            Plugin.Log.LogWarning("[narrate] dialog screen never opened - aborting visit (bind Debug.AbortVisitKey in the config if it stays stuck)");
            Abort(app);
            yield break;
        }
        // 稳态构图取证：二次进入若视角漂移，这一行就是对账依据
        var cm = EFT.CameraControl.CameraManager.Instance;
        if (cm != null && cm.Camera != null)
            Plugin.Log.LogInfo($"[narrate] steady camera: pos={cm.Camera.transform.position} rot={cm.Camera.transform.eulerAngles} fov={cm.Camera.fieldOfView:0.##}");
        ForeignLights.Mute();   // 玩家身上的枪灯等房间以外的灯：访问期关掉（坑 #111），退出恢复
    }

    /// 房间 + 公共场景就位后的兼容层一条龙（M10）。顺序有讲究：先修房间材质，再重启公共场景的 AmbientLight/贴花管理器——
    /// 反过来会让 StaticDeferredDecalRenderer 在坏材质上 OnDisable，实机连续抛 NRE。
    static void Stage(Scene scene, Scene common)
    {
        // 每一步各自兜异常（09-07 终审）：以前一步抛出整条 Run 协程就死，后面的季节开关 / 音频改挂 / 对话屏等待全跳过，画面黑、无声、不自动中止
        Step("lighting", () => SceneLighting.Apply(scene));
        Step("common shaders", () =>
        {
            if (common.isLoaded)
                foreach (var root in common.GetRootGameObjects()) SceneShaders.Fix(root);
            SceneShaders.ReportMisses();
        });
        Step("ambient snapshot", AmbientDrawGuard.RebuildSnapshot);   // 战局加载流程有这步、访问路径没有——不补的话二次进入遍历 null 每帧 NRE（坑 #95）
        foreach (var s in new[] { scene, common })
        {
            Step("season", () => SeasonGate.Apply(s));              // 1.1 的 SeasonEnvironment 按当前季节只留一组物件（09-06：不开关的话 Skier 冬夏两组同时在场）
            Step("audio remap", () => AudioRouting.Remap(s));       // bundle 音源挂的是 1.1 混音台死拷贝，不改挂游戏主混音台就永远没声
            Step("ambient audio", () => AudioRouting.EnsureAmbient(s));   // 场景里没有音总控（NPCSceneAudioController）时兜底开环境音组（坑 #101 的遗留防护，本管线的包有总控就不插手）
            Step("relight", () => SceneRelight.Promote(s));         // 1.1 的灯全是顶点光，0.16 延迟管线无视——提升为像素光才亮
        }
        Step("glass", () => GlassProbe.Report(scene));             // 09-07：Standard 预乘透明材质光滑度封顶 0.5（Mechanic 镜片一团黑，见 GlassProbe 注释）
    }

    static void Step(string name, System.Action act)
    {
        try { act(); }
        catch (System.Exception e) { Plugin.Log.LogError($"[narrate] stage '{name}' failed (continuing): {e}"); }
    }

    /// <summary>
    /// 0.16 引擎在 `VendorScenePresets.commonScenes` 里写死了公共场景 `Vendors_Scripts`，`Load` 按名字加载、`UnloadAll` 按名字卸载。
    /// 1.1 的 Jaeger / Peacekeeper / Skier 用的是各自专属的 `Vendors_X_Scripts`（多了日光 / 天气 / 大气，其余内容与公共的相同），
    /// 不是在公共那份之上再叠一份——所以这里把表里那一项**改名**而不是追加；每次访问前设一次，退出时引擎按同一个名字卸。
    /// </summary>
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

    /// <summary>访问在 `_game` 建好之前就失败时引擎不会走 Hide，`NarrateHideGuard.Prefix` 那套还原也就不跑（09-07 终审）：
    /// 贴图 mip 上限留在 0、菜单 3D 环境藏着、1.1 加载屏挂着、枪灯关着。这里把 Visit()/相机前缀之前改过的全局状态自己还回来。</summary>
    static void LocalCleanup()
    {
        try
        {
            ForeignLights.Restore();
            TexturePin.Restore();
            NarrateLoading.ForceClose();
            TabRouter.UnwatchNarrate();
            SceneLighting.Release();   // 含 Visibility.Environment(true) + active scene 还原
            Plugin.Log.LogInfo("[narrate] local state restored after aborted visit");
        }
        catch (System.Exception e) { Plugin.Log.LogError("[narrate] local cleanup failed: " + e); }
    }

    public static void EnsureMenu()
    {
        // 当前相机由原生 CameraManager 创建，必须保留到 PlayerCameraController.OnDestroy，
        // 让 CameraManager.Reset 完整释放效果订阅和相机对象；提前置空会把相机泄漏到下一次访问。
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

    // 原生商人映射表缺两位: Mechanic(5a7c2eca...)没进正反两表, Jaeger(5c0647fd...)在本 build 枚举里
    // 没有命名成员——12 是 1.0 官方枚举表核对出的数值
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
