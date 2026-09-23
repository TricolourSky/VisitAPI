using System;
using HarmonyLib;
using VisitAPI.ChapterUI;

namespace VisitAPI.Native;

public static class VisitPatches
{
    static readonly Type[] All =
    {
        typeof(NarrateHideGuard), typeof(NarrateGameHideGuard), typeof(NarrateSpawnGuard),
        typeof(NarrateCameraBypass), typeof(NarrateFovLock), typeof(NarrateSetFovLock), typeof(CameraSafety), typeof(UpscalerGuard),
        typeof(PrismExposurePin),
        typeof(PostChain.Before),
        typeof(DecalGuard), typeof(AmbientDrawGuard),
        typeof(ReflectionPin.AuthoredIntensity),
        typeof(NarrateDialogEntryGuard), typeof(NarrateSwitchGuard),
        typeof(NarrateRandomGuard), typeof(NarrateQuestGhostGuard), typeof(NarrateDialogBuildGuard),
        typeof(NarrateEmbedSwitchGuard),
        typeof(NarrateNpcVariantShuffle),
        typeof(SubtitleDiag.LineStart), typeof(SubtitleDiag.Shown), typeof(SubtitleDiag.Ended),
        typeof(NarrateHandoverWindow), typeof(NarrateHandoverWindow.UsePicked),
        typeof(NarrateChoiceWindow),
        typeof(NarrateAudioGuard), typeof(DialogScreenCloseGuard), typeof(DialogScreenTracker), typeof(WhitelistPatch),
        typeof(FirGuard), typeof(NarrateNpcGuard),
        typeof(TalkButton),
        typeof(NarrateLoadingShow), typeof(NarrateLoadingClose),
        typeof(ChapterTab), typeof(ChapterTab.ShowPatch),
        typeof(ChapterChain.Changed), typeof(ChapterChain.Added),
        typeof(QuestNotify), typeof(QuestConditionNotify), typeof(ChapterBanner.SoundPatch),
        typeof(QuestLootGate),
        typeof(VariableGroups.OnSet),
        typeof(BannerHost.CloseGuard),
        typeof(StoryList.SideList), typeof(StoryList.TraderList), typeof(StoryList.TraderQuestList),
        typeof(AnyOfQuest), typeof(AnyOfVisibility), typeof(DialogOnlyButton),
        typeof(TraderBadge.CardShow), typeof(TraderBadge.CardUpdate), typeof(TraderBadge.TaskBarAwake), typeof(TraderBadge.ScreenChanged),
        typeof(StoryMapLock),
        typeof(LockedTraderSelect),
        typeof(QuestZoneSpawn),
        typeof(HideoutAudioRestore),
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
