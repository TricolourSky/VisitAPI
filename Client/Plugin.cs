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
    public const string Version = "1.3.3";
    public static Plugin Instance;
    public static ManualLogSource Log;
    public static ConfigEntry<string> Language;
    public static ConfigEntry<float> TalkOffsetX;
    public static ConfigEntry<float> TalkOffsetY;
    public static ConfigEntry<bool> ShowUnstarted;
    public static ConfigEntry<float> CustomChapterOrder;
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
    public static ConfigEntry<bool> CallBadge;
    public static ConfigEntry<bool> HandoverBadge;

    void Awake()
    {
        Instance = this;
        Log = base.Logger;
        Language = Config.Bind("General", "Language", "auto", "界面语言 | UI language: auto / zh / en");
        TalkOffsetX = Config.Bind("TalkButton", "OffsetX", 0f, "访问按钮水平偏移 | Visit button X offset");
        TalkOffsetY = Config.Bind("TalkButton", "OffsetY", 0f, "访问按钮垂直偏移 | Visit button Y offset");
        ShowUnstarted = Config.Bind("Chapter", "ShowUnstartedChapters", false, "剧情页显示未开始的章节 | Show unstarted chapters");
        CustomChapterOrder = Config.Bind("Chapter", "CustomChapterOrder", 100f,
            "自制章节在剧情页的排序位次，小的在前。剧情页先按解锁先后排，解锁时间相同时才看这个值。官方章节是 1～10，默认 100 排在它们后面 | Sort position of custom chapters on the story page, smaller first. The page sorts by unlock time first; this value only breaks ties. Official chapters are 1-10; the default 100 places custom chapters after them");
        HideStory = Config.Bind("Chapter", "HideStoryQuestsInLists", true, "剧情任务不进普通任务列表 | Hide story quests in regular lists");
        LevelCamera = Config.Bind("Narrate", "LevelCamera", true,
            "访问商人时相机保持水平，与 1.1 房间的机位一致 | Keep the visit camera level to match the 1.1 room camera");
        PixelLights = Config.Bind("Narrate", "PixelLights", true,
            "商人房间的灯光按像素光渲染，灯罩的光斑形状才正确 | Render room lights as pixel lights so lamp shapes appear correctly");
        Fov = Config.Bind("Narrate", "Fov", 50f,
            "访问商人房间时的视野 | Visit camera field of view");
        DecalDirect = Config.Bind("Narrate", "DecalDirect", false,
            "逐个直接绘制房间贴花。默认关闭，打开会重复绘制 | Draw room decals one by one. Off by default; turning it on draws them twice");
        ShaderSource = Config.Bind("Narrate", "ShaderSource", "game",
            "房间材质使用的着色器：game = 游戏自带的同名着色器，bundle = 房间包里的 1.1 着色器 | Shaders for room materials: game = the game's own shaders, bundle = the 1.1 shaders in the room pack");
        DimReflection = Config.Bind("Narrate", "DimReflection", true,
            "关闭商人房间的环境反射和雾，避免画面整体发亮 | Mute environment reflection and fog in trader rooms so the picture is not washed out");
        AmbientReflection = Config.Bind("Narrate", "AmbientReflection", true,
            "屏幕环境光按 1.1 房间设定的强度渲染 | Use the 1.1 room's screen-space ambient intensity");
        CoordKey = Config.Bind("Debug", "CoordKey", KeyboardShortcut.Empty,
            "按下时把当前相机坐标写进日志，编写剧本触发点时使用。默认不绑定 | Write the camera position to the log, for placing script triggers (unbound by default)");
        AbortVisitKey = Config.Bind("Debug", "AbortVisitKey", KeyboardShortcut.Empty,
            "强制退出卡住的商人访问。默认不绑定 | Force-exit a stuck trader visit (unbound by default)");
        CallBadge = Config.Bind("Badge", "CallBadge", true,
            "商人有话要说时，在商人卡片和顶栏昵称旁显示金色电话角标 | Show a gold phone badge on the trader card and next to your nickname when a trader has something to say");
        HandoverBadge = Config.Bind("Badge", "HandoverBadge", true,
            "商人卡片使用 1.1.5 的角标：蓝色可上交、绿色可接、白色已完成 | Use 1.1.5 trader card badges: blue hand-over, green available, white completed");
        Loc.Mode = Language.Value;
        Language.SettingChanged += delegate { Loc.Mode = Language.Value; };
        Loc.GameCulture = () => LocalizationManager.Instance?.Culture;
        DlgLoc.Picker = Loc.Pick;
        VisitPatches.ApplyAll(new Harmony("com.sora.visitapi"));
        QuestFlags.Prefetch();
        QuestZones.Prefetch();
        Log.LogInfo($"VisitAPI {Version} loaded (SPT 4.1.x: NarrateSystem + 章节UI + 触发器)");
    }

    void Update()
    {
        if (CoordKey.Value.MainKey != KeyCode.None && CoordKey.Value.IsDown()) DialogDebug.OnCoordKey();
        if (AbortVisitKey.Value.MainKey != KeyCode.None && AbortVisitKey.Value.IsDown()) { Log.LogWarning("[narrate] abort key pressed"); NarrateEntry.Abort(); }
        TriggerHost.Tick();
    }
}
