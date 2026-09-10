using System;
using HarmonyLib;
using VisitAPI.ChapterUI;

namespace VisitAPI.Native;

/// <summary>
/// 逐个上补丁：某个类挂不上只损失那一个功能并留一条 Error，不再 PatchAll 一根绳全断。
/// 新补丁类写完必须登记到这张表，否则等于没生效。
/// </summary>
public static class VisitPatches
{
    static readonly Type[] All =
    {
        // ── Narrate 生命周期 ──
        typeof(NarrateHideGuard), typeof(NarrateGameHideGuard), typeof(NarrateSpawnGuard),
        // ── 相机 ──
        typeof(NarrateCameraBypass), typeof(NarrateFovLock), typeof(NarrateSetFovLock), typeof(CameraSafety), typeof(UpscalerGuard),
        typeof(PrismExposurePin),   // 坑 #108：EnvironmentManager 每帧覆写 Prism 曝光，访问期钉成 1.1 相机预制体的值
        // ── 资产修复 ──
        typeof(DecalGuard), typeof(AmbientDrawGuard),
        typeof(ReflectionPin.AuthoredIntensity),   // 坑 #119/#124：屏幕环境光按 1.1 写的 0.2 走，不用 SSR 开着时的 1
        // ── 零售对话数据补全 ──
        typeof(NarrateDialogEntryGuard), typeof(NarrateSwitchGuard),
        typeof(NarrateRandomGuard), typeof(NarrateQuestGhostGuard), typeof(NarrateDialogBuildGuard),
        // ── 兜底安全网 ──
        typeof(NarrateAudioGuard), typeof(DialogScreenCloseGuard), typeof(DialogScreenTracker), typeof(WhitelistPatch),
        typeof(FirGuard), typeof(NarrateNpcGuard),
        // ── UI 入口 ──
        typeof(TalkButton),
        typeof(NarrateLoadingShow), typeof(NarrateLoadingClose),   // 2026-09-05：访问加载屏换成 1.1 的商人加载屏
        // ── 章节剧情（阶段二）──
        typeof(ChapterTab), typeof(ChapterTab.ShowPatch),
        typeof(ChapterChain.Changed), typeof(ChapterChain.Added),
        typeof(QuestNotify), typeof(QuestConditionNotify), typeof(ChapterBanner.SoundPatch),
        typeof(StoryList.SideList), typeof(StoryList.TraderList), typeof(StoryList.TraderQuestList),
        typeof(AnyOfQuest), typeof(AnyOfVisibility), typeof(DialogOnlyButton),   // 09-10：AnyOfVisibility = 二选一组的显示条件
        // 2026-09-05：光影大修的 5 组取证探针 + 现场取证器 NarrateDiag 已随大修结束拆除，
        // 代码封存在 Narrate\NarrateDiag.cs.parked（要再取证就改回 .cs 并把这几行加回来）。
        // 同批封存：DecalArray.cs（坑 #114 已判作废）、PostTransplant.cs（补偿模式专用）。
        // G21/G22 未读徽章：SORA 2026-09-02 撤回待后议，代码封存在 ChapterUI\UnreadBadge.cs.parked
    };

    public static void ApplyAll(Harmony harmony)
    {
        var ok = 0;
        foreach (var t in All)
        {
            try { harmony.CreateClassProcessor(t).Patch(); ok++; }
            catch (Exception e) { Plugin.Log.LogError($"[patch] {t.Name} 挂载失败: {e}"); }
        }
        Plugin.Log.LogInfo($"[patch] {ok}/{All.Length} 组补丁已挂载");
    }
}
