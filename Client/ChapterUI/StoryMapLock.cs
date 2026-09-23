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
    static readonly Dictionary<string, bool> _lastLocked = new();

    static void Prefix(MatchMakerSelectionLocationScreen __instance)
    {
        try { Apply(Traverse.Create(__instance).Field<IEftSession>("_session").Value); }
        catch (Exception e) { Plugin.Log.LogError("[maplock] 剧情地图锁计算失败（选图界面照原样）: " + e); }
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
            if (gates == null || gates.Count == 0) continue;
            if (_lastLocked.TryGetValue(loc._Id, out var last) && last == locked) continue;
            _lastLocked[loc._Id] = locked;
            Plugin.Log.LogInfo($"[maplock] {loc.Id}（{loc._Id}）{(locked ? "剧情未到，锁住" : "已解锁")}：由任务 [{string.Join(", ", gates)}] 解锁");
        }
    }

    static bool Done(string questId, Profile profile)
    {
        var quest = ChapterChain.Controller?.Quests?.GetConditional(questId);
        if (quest != null) return quest.QuestStatus == EQuestStatus.Success;
        return profile?.QuestsData?.Any(q => q.Id == questId && q.Status == EQuestStatus.Success) == true;
    }
}
