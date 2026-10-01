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
    public const string Version = "1.3.5";
    public static Plugin Instance;
    public static ManualLogSource Log;
    // 09-25 SORA：BepInEx 设置页只留剧情页的三个个人偏好。其余（访问画面的 7 个调参项、2 个调试热键、访问按钮偏移、界面语言、
    // 三个 1.1 功能开关）全部去掉，固定成原来的默认值：画面按 1.1 定好的效果、界面语言跟随游戏、1.1 的角标 / 邀请信永远开着。
    // 10-01 例外：[Badge] 的两个开关放回来——GitHub 卡顿 issue 里用户靠 HandoverBadge=false 关掉最重的上交检查保帧数，
    // 0925 精简把开关删掉后他的设置失效、卡顿回归。键名照 1.3.3 原样，老配置文件直接生效；关掉 = 角标连同它的检查一起停
    public static ConfigEntry<bool> ShowUnstarted;
    public static ConfigEntry<float> CustomChapterOrder;
    public static ConfigEntry<bool> HideStory;
    public static ConfigEntry<bool> CallBadge;
    public static ConfigEntry<bool> HandoverBadge;

    void Awake()
    {
        Instance = this;
        Log = base.Logger;
        ShowUnstarted = Config.Bind("Chapter", "ShowUnstartedChapters", false, "剧情页显示未开始的章节 | Show unstarted chapters");
        CustomChapterOrder = Config.Bind("Chapter", "CustomChapterOrder", 100f,
            "自制章节在剧情页的排序位次，小的在前（章节包里自己写了 order 的以包为准）。原版 1.1 章节固定是 1～9，默认 100 排在它们后面 | Sort position of custom chapters on the story page, smaller first (a chapter pack's own order wins). Official 1.1 chapters are fixed at 1-9; the default 100 places custom chapters after them");
        HideStory = Config.Bind("Chapter", "HideStoryQuestsInLists", true, "剧情任务不进普通任务列表 | Hide story quests in regular lists");
        CallBadge = Config.Bind("Badge", "CallBadge", true, "商人头上的金色电话角标；关掉连同它的检查一起停 | Gold call badge on traders; off also skips its checks");
        HandoverBadge = Config.Bind("Badge", "HandoverBadge", true, "商人头上的可上交角标；这项检查最重，商人页面卡顿可先关它 | Hand-over badge on traders; heaviest check - turn off first if the trader screen stutters");
        Loc.Mode = "auto";
        Loc.GameCulture = () => LocalizationManager.Instance?.Culture;
        // .dlg 解析警告在插件里只进日志（DialogLoader），日志默认英文（SORA 09-25）——固定取英文那段；编辑器那边照旧按自己的语言设置
        DlgLoc.Picker = (zh, en) => en;
        var harmony = new Harmony("com.sora.visitapi");
        VisitPatches.ApplyAll(harmony);
        QuestFlags.Prefetch();
        QuestZones.Prefetch();
        StorySpawns.Prefetch();
        InviteWatcher.Start();
        VisitConsole.Register();   // 09-24：游戏控制台里的 visit_setvar / visit_getvar / visit_rescan
    }

    // 09-28 SORA：日志只在出问题时打，正常流程一条不写。兜底路径里捕获的异常同一处只报一次，免得每帧 / 每次刷新刷屏
    static readonly System.Collections.Generic.HashSet<string> _warnedOnce = new();

    public static void WarnOnce(string key, string message)
    {
        lock (_warnedOnce) if (!_warnedOnce.Add(key)) return;
        Log.LogWarning(message);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F11)) CoordKey.Print();   // 09-27 SORA：改回按 F11 取坐标（见 CoordKey）
        TriggerHost.Tick();
    }
}
