using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.Hideout;
using EFT.Quests;

namespace VisitAPI.Native;

public static class QuestOps
{
    static readonly HashSet<string> _busy = new();

    public static QuestController Resolve()
    {
        QuestController hideout = Singleton<HideoutRepresentation>.Instance?._questController;
        var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        if (hideout != null && world != null && Raid.IsHideout(world.LocationId)) return hideout;
        return GamePlayerOwner.MyPlayer?.QuestController ?? hideout;
    }

    public static void Accept(QuestController qc, Quest quest, string tag) => Run(qc, quest, true, tag);

    public static void Finish(QuestController qc, Quest quest, string tag) => Run(qc, quest, false, tag);

    public static bool SetStatus(QuestController qc, Quest quest, EQuestStatus want, string tag)
    {
        if (_busy.Contains(quest.Id)) { Plugin.Log.LogWarning($"[quest] {tag} 想把 {quest.Id} 写成 {want}，但它的事务在途，跳过"); return false; }
        if (!qc.TryExecuteTransition(quest, want)) qc.SetConditionalStatus(quest, want);
        return true;
    }

    static void Run(QuestController qc, Quest quest, bool accept, string tag)
    {
        if (!_busy.Add(quest.Id)) { Plugin.Log.LogInfo($"[quest] {quest.Id} 已在途（{tag} 重复发起，忽略）"); return; }
        Plugin.Instance.StartCoroutine(Later(qc, quest, accept, tag));
    }

    static IEnumerator Later(QuestController qc, Quest quest, bool accept, string tag)
    {
        yield return null;
        var what = accept ? "accept" : "finish";
        var skip = accept ? quest.QuestStatus != EQuestStatus.AvailableForStart : quest.QuestStatus >= EQuestStatus.Success;
        Task task = null;
        Exception thrown = null;
        if (!skip)
            try { task = accept ? qc.AcceptQuest(quest, runNetworkTransaction: true) : (Task)qc.FinishQuest(quest, runNetworkTransaction: true); }
            catch (Exception e) { thrown = e; }
        while (task != null && !task.IsCompleted) yield return null;
        _busy.Remove(quest.Id);
        if (thrown != null) { Plugin.Log.LogWarning($"[quest] {tag} {what} {quest.Id} 引擎入口抛异常: {thrown.Message}"); yield break; }
        if (task == null) { Plugin.Log.LogInfo($"[quest] {tag} {what} {quest.Id}：等一帧后状态是 {quest.QuestStatus}，不该发了，没发"); yield break; }
        var error = task.IsFaulted ? task.Exception?.GetBaseException().Message : LogicalError(task);
        if (error != null) Plugin.Log.LogWarning($"[quest] {tag} {what} {quest.Id} 被拒: {error}（现在是 {quest.QuestStatus}）");
        else { Plugin.Log.LogInfo($"[quest] {tag} {what} {quest.Id} -> {quest.QuestStatus}"); ChapterEvents.Raise(); }
    }

    static string LogicalError(Task task)
    {
        var result = task.GetType().GetProperty("Result")?.GetValue(task);
        if (result == null) return null;
        var type = result.GetType();
        var bad = (type.GetProperty("Failed")?.GetValue(result) as bool? == true)
               || (type.GetProperty("Succeed")?.GetValue(result) as bool? == false);
        if (!bad) return null;
        return type.GetProperty("Error")?.GetValue(result)?.ToString() ?? "engine refused";
    }
}
