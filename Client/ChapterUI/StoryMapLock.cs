using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.Quests;
using EFT.UI.Matchmaker;
using HarmonyLib;

namespace VisitAPI.Native;

[HarmonyPatch(typeof(MatchMakerSelectionLocationScreen), nameof(MatchMakerSelectionLocationScreen.ShowInternal))]
public static class StoryMapLock
{
    static readonly MongoID Flag = new("766973697461706900000001");
    static readonly Dictionary<string, bool> _original = new();

    /// ____session = 选图界面的私有字段 _session，由 Harmony 注入（10-24，旧 B15）。
    /// Harmony 的规矩是「三个下划线 + 字段原名」，字段本身带一个下划线，所以是四个——10-24 写成三个，补丁整个没装上（10-25 日志 No such field，当天修）
    static void Prefix(object ____session)
    {
        try { Apply(____session as IEftSession); }
        catch (Exception e) { Plugin.Log.LogError("[maplock] Story map lock calculation failed (map selection left as-is): " + e); }
    }

    static void Apply(IEftSession session)
    {
        var locations = session?.LocationSettings?.locations;
        if (locations == null) return;
        var profile = session.Profile;
        var story = profile?.ProfileVariables != null && profile.ProfileVariables.GetVariableValue(Flag) == 1;
        foreach (var loc in locations.Values)
        {
            if (string.IsNullOrEmpty(loc?._Id)) continue;
            if (!_original.TryGetValue(loc._Id, out var orig)) _original[loc._Id] = orig = loc.Locked;
            var gates = story ? QuestFlags.LocationGates(loc._Id) : null;
            var locked = gates != null && gates.Count > 0 && !gates.Any(id => Done(id, profile));
            loc.Locked = orig || locked;
        }
    }

    static bool Done(string questId, Profile profile)
    {
        var quest = ChapterChain.Controller?.Quests?.GetConditional(questId);
        if (quest != null) return quest.QuestStatus == EQuestStatus.Success;
        return profile?.QuestsData?.Any(q => q.Id == questId && q.Status == EQuestStatus.Success) == true;
    }
}
