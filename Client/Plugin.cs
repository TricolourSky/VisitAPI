using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT;
using HarmonyLib;
using UnityEngine;
using VisitAPI.Dialog;
using VisitAPI.Native;

namespace VisitAPI;

[BepInPlugin("com.sora.visitapi", "VisitAPI", Version)]
public class Plugin : BaseUnityPlugin
{
    public const string Version = "1.3.0";
    public static Plugin Instance;
    public static ManualLogSource Log;
    public static ConfigEntry<string> Language;
    public static ConfigEntry<float> TalkOffsetX;
    public static ConfigEntry<float> TalkOffsetY;
    public static ConfigEntry<bool> ShowUnstarted;
    public static ConfigEntry<bool> HideStory;
    public static ConfigEntry<float> Fov;
    public static ConfigEntry<bool> PixelLights;
    public static ConfigEntry<bool> LevelCamera;
    public static ConfigEntry<bool> DecalDirect;
    public static ConfigEntry<string> ShaderSource;
    public static ConfigEntry<bool> DimReflection;
    public static ConfigEntry<bool> AmbientReflection;
    public static ConfigEntry<KeyboardShortcut> CoordKey;
    public static ConfigEntry<KeyboardShortcut> AbortVisitKey;

    void Awake()
    {
        Instance = this;
        Log = base.Logger;
        Language = Config.Bind("General", "Language", "auto", "界面语言 | UI language: auto / zh / en");
        TalkOffsetX = Config.Bind("TalkButton", "OffsetX", 0f, "访问按钮水平偏移 | Visit button X offset");
        TalkOffsetY = Config.Bind("TalkButton", "OffsetY", 0f, "访问按钮垂直偏移 | Visit button Y offset");
        ShowUnstarted = Config.Bind("Chapter", "ShowUnstartedChapters", false, "剧情页显示未开始的章节 | Show unstarted chapters");
        HideStory = Config.Bind("Chapter", "HideStoryQuestsInLists", true, "剧情任务不进普通任务列表 | Hide story quests in regular lists");
        LevelCamera = Config.Bind("Narrate", "LevelCamera", true,
            "相机俯仰归零：玩家眼睛带 3.76° 下俯，1.1 房间自带的机位标记是俯仰 0（两张图逐像素量过，差的就是这 80 像素）| Level the visit camera pitch");
        PixelLights = Config.Bind("Narrate", "PixelLights", true,
            "把房间的顶点光全提成像素光（顶点光在 0.16 延迟管线里不吃 cookie，1.1 的灯罩光斑形状出不来）| Promote the room's vertex lights to pixel lights");
        Fov = Config.Bind("Narrate", "Fov", 50f,
            "访问房间的视野 | 50 是实机验收过的构图值（1.1 相机预制体写的是 75）| Visit camera field of view");
        DecalDirect = Config.Bind("Narrate", "DecalDirect", false,
            "贴花改成逐个 DrawMesh 画。**默认关**——坑 #116 的立论（0.16 画不出贴花）是在剥了法线的包上取的证，法线补回来后 0.16 自己就画得出来，再直画一遍等于画两遍（09-05 SORA 实机：关掉才和 1.1 正式版一样，坑 #125）| Draw static decals one by one");
        // ── 下面三项 09-05 从「拆除」改回「保留」：坑 #124。它们不是「违背 1.1 的数据」，
        //    而是「同一个数在 0.16 的渲染器里跑出来不是 1.1 的样子」——实机 A/B 判的，别再按 1.1 的字面值删。
        ShaderSource = Config.Bind("Narrate", "ShaderSource", "game",
            "材质用谁的 shader | game=同名换成 0.16 自己的（实机验收过的观感）；bundle=包里带的 1.1 二进制 shader | Which shader implementation to use");
        DimReflection = Config.Bind("Narrate", "DimReflection", true,
            "环境反射按 0 走、雾关掉（1.1 authored 是 1/开，但 0.16 的反射源是**烘焙时那张白天蓝空**的 HDR 立方图，强度 1 会把满屋光泽面垫亮，坑 #124）| Mute environment reflection and fog");
        AmbientReflection = Config.Bind("Narrate", "AmbientReflection", true,
            "屏幕环境光按 1.1 场景写的强度 0.2 走（0.16 见 SSR 开着就取 ReflectionIntensitySSR=1，等于给全画面垫 5 倍底光，坑 #119/#124）| Pin the screen-ambient pass to the authored reflection intensity");
        // 09-07 终审：热键默认**不绑**（SORA 09-07 明令：F 键被别的插件占满，热键取证全部拆除）。
        // 作者要打触发点坐标、或访问卡死要逃生，自己在配置里绑一个键；默认 None 时 Update 里一次都不查键盘。
        CoordKey = Config.Bind("Debug", "CoordKey", KeyboardShortcut.Empty,
            "打印当前相机坐标到日志（.dlg 触发点填坐标用，与判距同基准）。默认不绑 | Log the camera position for trigger authoring (unbound by default)");
        AbortVisitKey = Config.Bind("Debug", "AbortVisitKey", KeyboardShortcut.Empty,
            "强制退出卡住的商人访问。默认不绑 | Force-exit a stuck trader visit (unbound by default)");
        Loc.Mode = Language.Value;
        Language.SettingChanged += delegate { Loc.Mode = Language.Value; };
        Loc.GameCulture = () => LocalizationManager.Instance?.Culture;
        DlgLoc.Picker = Loc.Pick;
        VisitPatches.ApplyAll(new Harmony("com.sora.visitapi"));
        QuestFlags.Prefetch();
        Log.LogInfo($"VisitAPI {Version} loaded (SPT 4.1.x: NarrateSystem + 章节UI + 触发器)");
    }

    void Update()
    {
        if (CoordKey.Value.MainKey != KeyCode.None && CoordKey.Value.IsDown()) DialogDebug.OnCoordKey();
        if (AbortVisitKey.Value.MainKey != KeyCode.None && AbortVisitKey.Value.IsDown()) { Log.LogWarning("[narrate] abort key pressed"); NarrateEntry.Abort(); }
        TriggerHost.Tick();
    }
}
