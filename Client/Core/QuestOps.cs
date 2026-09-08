using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.Hideout;
using EFT.Quests;

namespace VisitAPI.Native;

/// <summary>
/// 任务状态写入的唯一出口（T-6）。以前触发器、对话 setstatus、章节自动链各自发事务，
/// 「双交任务红字」就出在两路同时对一条任务发 FinishQuest —— 现在全走这里，靠在途表去重：
/// 谁先登记谁发，后来的直接忽略；事务发完（成功或失败）才撤登记。
/// _busy 只在事务在途时有内容、事后自清（引擎入口同步抛异常也撤登记），无需换档复位。
/// </summary>
public static class QuestOps
{
    static readonly HashSet<string> _busy = new();   // 正在走网络事务的任务 id

    /// <summary>当前该用哪个任务控制器。**藏身处必须用 HideoutRepresentation 的 Backend 版**（真事务、进档案）——
    /// 藏身处 3D 里 MyPlayer 挂的是 QuestControllerClientLocalGame（LocalPlayer.cs:63），它的 AcceptQuest 只改内存、
    /// 不发服务端，退出即蒸发（2026-09-02 藏身处实测，坑 #98）。战局内 MyPlayer 的 LocalGame 才是正解（SPT 结算时回写）。</summary>
    public static QuestController Resolve()
    {
        QuestController hideout = Singleton<HideoutRepresentation>.Instance?._questController;
        var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        if (hideout != null && world != null && Raid.IsHideout(world.LocationId)) return hideout;
        return GamePlayerOwner.MyPlayer?.QuestController ?? hideout;
    }

    /// <summary>接任务（等一帧 + 状态复查 + 在途去重）。tag 只进日志，标明发起方。</summary>
    public static void Accept(QuestController qc, Quest quest, string tag) => Run(qc, quest, true, tag);

    /// <summary>交任务——发奖励/发邮件/同步服务端的真事务。状态不足时引擎自己会垫到「可提交」。</summary>
    public static void Finish(QuestController qc, Quest quest, string tag) => Run(qc, quest, false, tag);

    /// <summary>非事务的裸状态写（fail / 对话 setstatus）。任务在途时拒写，防止和事务互相踩。</summary>
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

    // 等一帧再发（铁律：不在引擎事件派发里改状态）。
    // accept 只在仍「可接」时发；finish 只要没到终态就发——引擎的 FinishQuest 自己会把状态垫到
    // 「可提交」（LocalGame 版逐字如此），上一版要求恰好等于 AvailableForFinish，状态被引擎回算
    // 刷掉就静默不交，藏身处实测踩过。
    static IEnumerator Later(QuestController qc, Quest quest, bool accept, string tag)
    {
        yield return null;
        var what = accept ? "accept" : "finish";
        var skip = accept ? quest.QuestStatus != EQuestStatus.AvailableForStart : quest.QuestStatus >= EQuestStatus.Success;
        Task task = null;
        Exception thrown = null;   // 引擎入口同步抛异常时协程会当场死掉，_busy 就永远撤不掉——这条任务本会话再也发不出去
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

    // 引擎的失败不走异常：IResult 看 Succeed，OperationResult 看 Failed（默认值 Error==null 算成功）。
    // 只认显式失败信号，读不出来就当成功——这里只为把「被拒」从静默变可见。
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
