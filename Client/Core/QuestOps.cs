using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.Hideout;
using EFT.Quests;
using HarmonyLib;

namespace VisitAPI.Native;

public static class QuestOps
{
    static readonly HashSet<string> _busy = new();

    /// <summary>09-26 F2：大厅那份任务控制器——原生主菜单建好、Run 完之后交给 HideoutRepresentation.SetInventoryController 的那一份（藏身处用的也是它）。
    /// 进战局时主菜单把它 Dispose（OnBeingMatchedHandler / Unsubscribe），这里跟着清空；战局、撤离、结算页、转移期间为空，回大厅重建后再登记。
    /// 不能拿 TarkovApplication._menuOperation 判：它要等整个主菜单流程（含 Run）结束才赋值。</summary>
    public static QuestControllerClientBackend Lobby { get; private set; }

    public static bool IsLobby(QuestController qc) => qc != null && ReferenceEquals(qc, Lobby);

    /// <summary>09-26 F2：只对「活的」任务控制器自动接 / 交 / 改状态：大厅那份；或战局（藏身处）里本机玩家那份、且这一局还在 Started。
    /// 撤离时原生 Stop 先把游戏状态改成 Stopping，GameEnd 里 OnGameSessionEnd 改任务状态、同一帧 CleanUp 就释放了战局控制器；
    /// 结算页、访问也各建一份临时的。对这些下单，引擎会在半拆的状态里跑（09-25 撤离瞬间 FinishQuest 空引用），
    /// 或者在撤离结算、转移途中往服务端发请求（F1）。</summary>
    public static bool IsLive(QuestController qc)
    {
        if (qc == null) return false;
        if (IsLobby(qc)) return true;
        if (qc is not QuestControllerClientLocalGame) return false;
        return Raid.Started && ReferenceEquals(GamePlayerOwner.MyPlayer?.QuestController, qc);
    }

    /// 藏身处里玩家身上那份 LocalGame 控制器：对它的事件要转到大厅那份去处理
    public static bool IsHideoutCopy(QuestController qc) => qc is QuestControllerClientLocalGame && Raid.InHideout;

    /// 界面各处拿「当前任务控制器」的回退链：链记下的 → 任务页给的 → 现算
    public static QuestController Current => ChapterChain.Controller ?? ChapterTab.Quests ?? Resolve();

    /// <summary>区域 / 刷兵 / 字幕的任务门：没写任务 = 不设门；任务不在任务书里 = 不放行；statuses 为空 = 任意状态都行
    ///（交互型触发点的门不走这里——它要求必须写 statuses，SORA 10-17 定）</summary>
    public static bool StatusIn(string questId, List<int> statuses)
    {
        if (string.IsNullOrEmpty(questId)) return true;
        var quest = Resolve()?.Quests?.GetConditional(questId);
        return quest != null && (statuses == null || statuses.Count == 0 || statuses.Contains((int)quest.QuestStatus));
    }

    internal static void OnLobby(QuestControllerClientBackend qc)
    {
        if (qc == null || ReferenceEquals(qc, Lobby)) return;
        Lobby = qc;
        ChapterChain.Controller = qc;
        ChapterChain.Rescan();
    }

    internal static void OnDisposed(QuestController qc)
    {
        if (IsLobby(qc)) Lobby = null;
        if (ReferenceEquals(qc, ChapterChain.Controller)) ChapterChain.Controller = null;
    }

    /// 09-26：战局外优先用大厅那份（访问时原生对话也用主菜单的控制器；MyPlayer 那份访问中是访问副本，大厅里可能是上一局留下的玩家）
    public static QuestController Resolve()
    {
        QuestController hideout = Singleton<HideoutRepresentation>.Instance?._questController;
        var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        if (hideout != null && world != null && Raid.IsHideout(world.LocationId)) return hideout;
        if (Raid.Now) return GamePlayerOwner.MyPlayer?.QuestController ?? hideout;
        return Lobby ?? GamePlayerOwner.MyPlayer?.QuestController ?? hideout;
    }

    public static void Accept(QuestController qc, Quest quest, string tag) => Run(qc, quest, Op.Accept, tag);

    public static void Finish(QuestController qc, Quest quest, string tag) => Run(qc, quest, Op.Finish, tag);

    /// 10-06：接下来紧接着交（一个完成条件都没有的剧情步骤，1.1 服务端开了就算完成）
    public static void AcceptThenFinish(QuestController qc, Quest quest, string tag) => Run(qc, quest, Op.AcceptThenFinish, tag);

    public static bool Busy(string questId) => questId != null && _busy.Contains(questId);

    enum Op { Accept, Finish, AcceptThenFinish }

    /// <summary>对话 setstatus: 和触发器改任务状态。1.3.4 B3：只改本地的话（TryExecuteTransition 在基类恒返回 false，
    /// 走的是 SetConditionalStatus = 本地 TransitionStatus + NotifyRewards），服务端不知道，重进游戏状态就回退了。
    /// 所以战局外（大厅 / 藏身处 / 访问，用的是 QuestControllerClientBackend）照原生的路走：
    ///   完成 → 原生 FinishQuest（还没接的先 AcceptQuest 再交）；
    ///   进行中（从可接）→ 原生 AcceptQuest；
    ///   失败 → 本地置失败 + 原生 FailConditional（发 QuestFail），和 LocalFail 共用「已发」记录，同一条只发一次；
    /// 其余状态（原生没有对应的请求）仍只改本地。战局里失败 / 完成由撤离结算上报，也只改本地。
    /// 09-26 F1：战局控制器 QuestControllerClientLocalGame 本身继承自 QuestControllerClientBackend，靠 !Raid.Now 挡不住（战局收尾时 AbstractGame 已释放）；
    /// 只有大厅那份走网络，其余活的控制器只改本地，不活的（撤离中、结算页、访问副本）不动。</summary>
    public static void SetStatus(QuestController qc, Quest quest, EQuestStatus want, string tag)
    {
        if (_busy.Contains(quest.Id)) { Plugin.Log.LogWarning($"[quest] {tag} wants to set {quest.Id} to {want}, but a transaction for it is in flight; skipped"); return; }
        if (!IsLive(qc)) { Plugin.Log.LogWarning($"[quest] {tag} wants to set {quest.Id} to {want}, but its quest controller is not live (raid ending, result screen or a visit copy); skipped"); return; }
        if (IsLobby(qc))
        {
            var now = quest.QuestStatus;
            switch (want)
            {
                case EQuestStatus.Success when now == EQuestStatus.Success:
                    return;
                case EQuestStatus.Success when now == EQuestStatus.AvailableForStart:
                    Run(qc, quest, Op.AcceptThenFinish, tag);
                    return;
                case EQuestStatus.Success when ChapterUI.ChapterStates.Running(now):
                    if (now == EQuestStatus.Started) qc.SetConditionalStatus(quest, EQuestStatus.AvailableForFinish);
                    Run(qc, quest, Op.Finish, tag);
                    return;
                case EQuestStatus.Started when now == EQuestStatus.AvailableForStart:
                    Run(qc, quest, Op.Accept, tag);
                    return;
                case EQuestStatus.Fail:
                case EQuestStatus.FailRestartable:
                case EQuestStatus.MarkedAsFailed:
                    Fail(Lobby, quest, want, tag);
                    return;
            }
        }
        if (!qc.TryExecuteTransition(quest, want)) qc.SetConditionalStatus(quest, want);
    }

    static void Fail(QuestControllerClientBackend qc, Quest quest, EQuestStatus want, string tag)
    {
        var claimed = LocalFail.Claim(quest.Id);
        if (quest.QuestStatus != want) qc.SetConditionalStatus(quest, want);
        if (!claimed) return;
        try
        {
            qc.FailConditional(quest);
            LocalFail.ServerNow(quest.Id, EQuestStatus.Fail);
            ChapterEvents.Raise();
        }
        catch (Exception e)
        {
            LocalFail.Unclaim(quest.Id);
            Plugin.Log.LogWarning($"[quest] {tag} {quest.Id} native FailConditional failed (local state only): {e.Message}");
        }
    }

    /// 10-06 意外证人实测（「只有在撤离战局之后这个任务目标才会出来」）：一条任务在自己「接」的那一下就满足了完成条件（空步骤、身上已有要找的物品），
    /// 嵌套的状态事件里链要求「交」，撞上在途标记直接丢的话也不留痕，战局里之后再没有事件来推，一直卡到回大厅整本重扫才交掉。
    /// 所以在途时来的「交」先记下，等这一笔做完接着交（「接」重复了就是真重复，直接丢）
    static readonly Dictionary<string, (QuestController qc, Quest quest, Op op, string tag)> _queued = new();

    static void Run(QuestController qc, Quest quest, Op op, string tag)
    {
        if (!_busy.Add(quest.Id))
        {
            if (op != Op.Accept) _queued[quest.Id] = (qc, quest, op, tag);
            return;
        }
        Plugin.Instance.StartCoroutine(Later(qc, quest, op, tag));
    }

    /// 09-24 审查低项：每一步最多等 60 秒，并用 finally 放开在途标记，不然引擎的任务永远不完成（或协程被打断）时标记不清，这条任务本局再也接不了
    static IEnumerator Later(QuestController qc, Quest quest, Op op, string tag)
    {
        try
        {
            yield return null;
            if (op != Op.Finish)
            {
                var ok = false;
                yield return Step(qc, quest, accept: true, tag, r => ok = r);
                if (!ok || op == Op.Accept) yield break;
                if (quest.QuestStatus == EQuestStatus.Started) qc.SetConditionalStatus(quest, EQuestStatus.AvailableForFinish);
            }
            yield return Step(qc, quest, accept: false, tag, _ => { });
        }
        finally
        {
            _busy.Remove(quest.Id);
            if (_queued.TryGetValue(quest.Id, out var next))
            {
                _queued.Remove(quest.Id);
                if (IsLive(next.qc) && next.quest.QuestStatus < EQuestStatus.Success) Run(next.qc, next.quest, next.op, next.tag);
            }
        }
    }

    static IEnumerator Step(QuestController qc, Quest quest, bool accept, string tag, Action<bool> result)
    {
        var what = accept ? "accept" : "finish";
        var skip = accept ? quest.QuestStatus != EQuestStatus.AvailableForStart : quest.QuestStatus >= EQuestStatus.Success;
        // 09-26 F2：排队到下一帧这段时间里战局可能已经结束（撤离时任务状态在 CleanUp 同一帧改），调原生前再核一次
        if (!skip && !IsLive(qc))
        {
            Plugin.Log.LogWarning($"[quest] {tag} {what} {quest.Id} dropped: its quest controller is not live (raid not started yet, raid ending, result screen or a visit copy)");
            result(false); yield break;
        }
        Task task = null;
        Exception thrown = null;
        if (!skip)
            try { task = accept ? qc.AcceptQuest(quest, runNetworkTransaction: true) : (Task)qc.FinishQuest(quest, runNetworkTransaction: true); }
            catch (Exception e) { thrown = e; }
        var deadline = UnityEngine.Time.realtimeSinceStartup + 60f;
        while (task != null && !task.IsCompleted && UnityEngine.Time.realtimeSinceStartup < deadline) yield return null;
        if (thrown != null) { Plugin.Log.LogWarning($"[quest] {tag} {what} {quest.Id} engine entry point threw: {thrown.Message}"); result(false); yield break; }
        if (task == null) { result(false); yield break; }
        if (!task.IsCompleted) { Plugin.Log.LogWarning($"[quest] {tag} {what} {quest.Id} no result after 60 s; clearing the in-flight flag (a late result from the engine still takes effect)"); result(false); yield break; }
        var error = task.IsFaulted ? task.Exception?.GetBaseException().Message : LogicalError(task);
        if (error != null) { Plugin.Log.LogWarning($"[quest] {tag} {what} {quest.Id} rejected: {error} (now {quest.QuestStatus})"); result(false); yield break; }
        LocalFail.ServerNow(quest.Id, accept ? EQuestStatus.Started : EQuestStatus.Success);
        ChapterEvents.Raise();
        result(true);
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

/// 09-26 F2：跟着原生主菜单登记 / 注销大厅那份任务控制器（见 QuestOps.Lobby）
public static class LobbyQuestController
{
    [HarmonyPatch(typeof(HideoutRepresentation), nameof(HideoutRepresentation.SetInventoryController))]
    public static class Register
    {
        static void Postfix(QuestControllerClientBackend questController)
        {
            try { QuestOps.OnLobby(questController); }
            catch (Exception e) { Plugin.Log.LogError("[quest] Registering the lobby quest controller failed (the main menu itself is unaffected): " + e); }
        }
    }

    [HarmonyPatch(typeof(QuestControllerClient), nameof(QuestControllerClient.Dispose))]
    public static class Disposed
    {
        static void Postfix(QuestControllerClient __instance)
        {
            try { QuestOps.OnDisposed(__instance); }
            catch (Exception e) { Plugin.Log.LogWarning("[quest] Tracking a disposed quest controller failed: " + e.Message); }
        }
    }
}
