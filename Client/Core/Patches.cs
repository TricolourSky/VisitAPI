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
        typeof(NarrateLocationIdOnAwake),
        // 10-24 合并：ChatScreen.Show 上页签 + 信头 + 皮肤一个后缀（ChatInviteTabs）；MenuTaskBar.Awake 上底栏电话 + 顶栏角标一个后缀（ChatInviteTaskbar）
        typeof(ChatInviteTabs), typeof(ChatInviteBadge),
        typeof(ChatInviteHeader.OnSelected), typeof(ChatInviteHeader.OnChanged),
        typeof(ChatInviteBubble), typeof(ChatInviteTaskbar), typeof(ChatPreviewEllipsis), typeof(ChatMessageStyle.TimerFormat),
        typeof(ButtonStubGuard), typeof(TweenStubGuard),
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
        typeof(TraderBadge.CardShow), typeof(TraderBadge.CardUpdate), typeof(TraderBadge.ScreenChanged),
        typeof(StoryMapLock),
        typeof(LockedTraderSelect),
        // 10-16 深夜：包里注册的商人没解锁就不出现在交易界面（Abyss 等「深渊的频道」解锁才露面）
        typeof(LockedTraderHide.Screen), typeof(LockedTraderHide.Merchants),
        // 10-24 合并：进图开局的区域生成 / 剧情刷兵 / 护卫 / 盯开章物品，一个补丁
        typeof(RaidStart),
        // 10-16 黑色军团护卫：护卫组对真人永远不记敌
        typeof(EscortGuards.NoHumanEnemy), typeof(EscortGuards.HumanNotEnemy),
        // 10-25 纯 API：护卫 / 突入兵的行为层自己托管（不再要 BigBrain）；原版大脑里对护卫添乱的几层不让接手
        typeof(BotLayers.Attach), typeof(BotLayers.CreateNode), typeof(EscortGuards.NativeLayers),
        // 10-16 晚：语音轮盘指挥护卫
        typeof(EscortCommands.Said),
        // 10-17 凌晨：护卫不许跟着 SAIN 跑去撤离（没装 SAIN 时 Prepare 为 false 自动跳过）
        typeof(EscortNoExtract),
        // 10-16 晚：主菜单左下角把干员邀请进队（只动面板的可见名单 + 订单）
        typeof(EscortParty.PanelShown), typeof(EscortParty.InviteWindow), typeof(EscortParty.VarChanged), typeof(EscortParty.Invite), typeof(EscortParty.Remove), typeof(EscortParty.GroupMenu), typeof(EscortParty.FriendMenu),
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
        // 10-06：开章线索改成任务物品后，按 1.1 的规矩决定谁能在战局里看到它
        typeof(ItemStart.Visible),
        // 10-16：跨剧本跳转（-> 商人id:节点）后对话窗的名字 / 头像换成说话的那家。补丁不是自动扫的，新写的类不登记在这里就不生效（实机踩过）
        typeof(SpeakerSwitch.Shown), typeof(SpeakerSwitch.Redrawn),
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
