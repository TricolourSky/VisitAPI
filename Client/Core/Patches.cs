using System;
using HarmonyLib;
using VisitAPI.ChapterUI;

namespace VisitAPI.Native;

public static class VisitPatches
{
    static readonly Type[] All =
    {
        typeof(NarrateHideGuard), typeof(NarrateGameHideGuard), typeof(NarrateSpawnGuard),
        typeof(VisitGameGuard.Create), typeof(VisitGameGuard.Construct), typeof(VisitGameGuard.NoRestart), typeof(VisitGameGuard.Disposed),
        typeof(NarrateCameraBypass), typeof(NarrateFovLock), typeof(NarrateSetFovLock), typeof(CameraSafety), typeof(UpscalerGuard),
        typeof(VisitCameraPin),
        typeof(PrismExposurePin),
        typeof(PostChain.Before),
        typeof(DecalGuard), typeof(AmbientDrawGuard),
        typeof(ReflectionPin.AuthoredIntensity),
        typeof(NarrateDialogEntryGuard), typeof(NarrateSwitchGuard),
        typeof(NarrateQuestGhostGuard), typeof(NarrateDialogBuildGuard),
        typeof(NarrateEmbedSwitchGuard),
        typeof(NarrateNpcVariantShuffle),
        typeof(NarrateHandoverWindow), typeof(NarrateHandoverWindow.UsePicked),
        typeof(NarrateChoiceWindow),
        typeof(NarrateAudioGuard), typeof(DialogScreenCloseGuard), typeof(DialogScreenTracker), typeof(WhitelistPatch),
        typeof(FirGuard), typeof(NarrateNpcGuard),
        typeof(NarrateLocationIdOnAwake), typeof(NarrateLocationIdOnStart),
        typeof(ChatInviteTabs), typeof(ChatInviteBadge),
        typeof(ChatInviteHeader.OnShow), typeof(ChatInviteHeader.OnSelected), typeof(ChatInviteHeader.OnChanged),
        typeof(ChatInviteBubble), typeof(ChatInviteTaskbar), typeof(ChatPreviewEllipsis), typeof(ChatMessageStyle.TimerFormat),
        typeof(VisitAPI.ChapterUI.ButtonStubGuard), typeof(VisitAPI.ChapterUI.TweenStubGuard),
        typeof(SubtitleReplay.ReplayOnShow), typeof(SubtitleReplay.IgnoreStaleEnd),
        typeof(OptionalConditions),
        typeof(TalkButton),
        typeof(NarrateLoadingShow), typeof(NarrateLoadingClose),
        typeof(ChapterTab), typeof(ChapterTab.ShowPatch),
        typeof(ChapterChain.Changed), typeof(ChapterChain.Added),
        typeof(QuestNotify), typeof(QuestConditionNotify), typeof(ChapterBanner.SoundPatch),
        typeof(VariableGroups.OnSet),
        typeof(BannerHost.CloseGuard),
        typeof(StoryList.SideList), typeof(StoryList.TraderList), typeof(StoryList.TraderQuestList),
        typeof(AnyOfQuest), typeof(AnyOfVisibility), typeof(DialogOnlyButton),
        typeof(TraderBadge.CardShow), typeof(TraderBadge.CardUpdate), typeof(TraderBadge.TaskBarAwake), typeof(TraderBadge.ScreenChanged),
        typeof(StoryMapLock),
        typeof(LockedTraderSelect),
        typeof(QuestZoneSpawn),
        typeof(StorySpawnStart),
        typeof(HideoutAudioRestore),
        // 1.3.4
        typeof(NarrateStartBroadcast), typeof(NarrateShowError),
        typeof(DialogActionWait),
        typeof(EyeFollowerGuard), typeof(EyeFollowerTarget), typeof(AmbianceGuard), typeof(NpcAnimations),
        // 09-26 SP-Mods 反馈：只对活的任务控制器下单（F1 / F2）
        typeof(LobbyQuestController.Register), typeof(LobbyQuestController.Disposed),
        // 10-01：内容包任务在战局内交时不当场发奖励，统一由服务端结算时发
        typeof(RaidRewardHold.Finish),
        // 10-02：商人说话那一段不重画对话窗（点选项后小框闪一下）
        typeof(SayBeatWindow),
    };

    public static void ApplyAll(Harmony harmony)
    {
        foreach (var t in All)
        {
            try { harmony.CreateClassProcessor(t).Patch(); }
            catch (Exception e) { Plugin.Log.LogError($"[patch] {t.Name} failed to apply: {e}"); }
        }
    }
}
