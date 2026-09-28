using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.Quests;
using HarmonyLib;
using UnityEngine;

namespace VisitAPI.Native;

/// <summary>09-24（SORA 实机：Prapor 对话里选「箱子留我这」之后 Kerman 没来信）：1.1 用全局变量做任务失败条件——「回收加固手提箱」679cdee4 的
/// Fail 条件是变量 68d82af4 = 1，由 Prapor 对话的 SetVariable 置位；失败之后 Kerman 的另一封邀请信 68e1a9e8 才解锁。
/// 0.16.9 引擎在大厅里发现失败条件满足时**只改本地状态、不通知服务端**：ConditionalController.OnConditionValueChanged 调 CheckForStatusChange 时传 canFail:false，
/// 不会走 FailConditional → QuestFail；登录时 ManageConditional 走 fromServer:true，本地成了 MarkedAsFailed。正式服由服务端自己判失败，
/// SPT 4.1 服务端只在别的任务完成时查 Quest 型失败条件，不认变量 → 重登又是 Started，失败支永远解不开。
/// 这里替引擎补上那一步：剧情任务、服务端还当它进行中、失败条件里有**已满足的 GlobalVariableValue** → 发引擎自己的 QuestFail 请求（同 FailConditional）。
/// 服务端照常记失败、发失败奖励（这条是 Prapor 声望扣减）、寄失败信、把因它失败而解锁的任务随响应下发。
/// 两处细节（09-24 第二次实机，失败记上了但 Kerman 那条没出现）：
/// ① 登录时任务书装载（ManageConditional）发生在 ProfileUpdatesHandler.Bind 之前，这时候发请求，响应里 profileChanges.quests 找不到档案更新器就丢了——
///    所以不在事件里直接发，排队等档案更新器登记好再发；② 发完再拉一次 /client/quest/list，任务书里没有的补进去，不指望响应那条路。
/// 响应后把本地状态定成 Fail——引擎的 ConditionQuest 只认精确状态，MarkedAsFailed 不算 Fail，依赖它的任务条件才会亮。每条任务只发一次。</summary>
public static class LocalFail
{
    static readonly Dictionary<string, EQuestStatus> _server = new(StringComparer.Ordinal);   // 任务书里每条任务服务端给的状态
    static readonly HashSet<string> _sent = new(StringComparer.Ordinal);
    static readonly List<(QuestControllerClientBackend qc, Quest quest)> _pending = new();
    static bool _pumping;

    /// <summary>ManageConditional 之前记下服务端给的状态：构造时 SetStatus 已把 questDataClass.Status 定成服务端值
    ///（大厅任务书 FromServer=false，不做 Fail→MarkedAsFailed 换算），ManageConditional 里的 CheckForStatusChange 才会按本地条件改它。</summary>
    public static void Record(Quest quest)
    {
        if (quest?.Template == null) return;
        _server[quest.Id] = quest.questDataClass?.Status ?? quest.QuestStatus;
    }

    public static void Check(QuestController qc, Quest quest)
    {
        if (!Failed(quest.QuestStatus)) return;
        // 战局里的失败由撤离结算上报，不插手。09-26 F1：战局控制器 QuestControllerClientLocalGame 也是 QuestControllerClientBackend，
        // 以前「is not Backend」挡不住它，战局中会发 QuestFail；现在只认大厅那份
        if (!QuestOps.IsLobby(qc) || qc is not QuestControllerClientBackend backend) return;
        if (!QuestFlags.IsStory(quest.Id) || _sent.Contains(quest.Id) || _pending.Any(p => p.quest.Id == quest.Id)) return;
        if (!ServerStillRunning(quest.Id)) return;
        if (!quest.GetConditions<ConditionGlobalVariableValue>(EQuestStatus.Fail).Any(c => Done(quest, c))) return;
        _pending.Add((backend, quest));
        if (_pumping) return;
        _pumping = true;
        Plugin.Instance.StartCoroutine(Pump());
    }

    /// <summary>1.3.4 B3：对话 setstatus / 触发器 fail 主动置失败时由 QuestOps 发原生 FailConditional（QuestFail）。
    /// SPT 的 FailQuest 不查当前状态，同一条发两次就扣两次罚、寄两封信，所以和这里共用一份「已发」记录：
    /// 返回 true = 由调用方来发（已登记成已发）；false = 这里已经发过 / 排着队要发，或服务端档案里没有这条任务的进行状态（发了只会白扣罚）。</summary>
    public static bool Claim(string questId)
    {
        if (string.IsNullOrEmpty(questId) || _sent.Contains(questId) || _pending.Any(p => p.quest.Id == questId)) return false;
        // SPT UpdateQuestState 只改档案 Quests 里已有的条目：可接 / 进行中 / 可交这三种才在里面；Locked 的发过去状态不落档，却照样扣罚寄信
        if (!_server.TryGetValue(questId, out var server)
            || (server != EQuestStatus.AvailableForStart && server != EQuestStatus.Started && server != EQuestStatus.AvailableForFinish)) return false;
        _sent.Add(questId);
        return true;
    }

    /// Claim 之后发送失败或被拒：放回去，允许以后再发
    public static void Unclaim(string questId) => _sent.Remove(questId);

    /// 服务端确认了新状态（接 / 交 / 失败成功之后），登录时记下的那份服务端状态跟着更新
    public static void ServerNow(string questId, EQuestStatus status)
    {
        if (!string.IsNullOrEmpty(questId)) _server[questId] = status;
    }

    static bool Failed(EQuestStatus st) => st == EQuestStatus.Fail || st == EQuestStatus.FailRestartable || st == EQuestStatus.MarkedAsFailed;

    static bool ServerStillRunning(string id) =>
        _server.TryGetValue(id, out var server) && (server == EQuestStatus.Started || server == EQuestStatus.AvailableForFinish);

    static IClientSession Session()
    {
        try { return Singleton<ClientApplication<IEftSession>>.Instance?.GetClientBackEndSession(); }
        catch { return null; }
    }

    /// 档案更新器（ProfileUpdatesHandler）是否已在会话里登记：登记前收到的 profileChanges.quests 会被 ApplyProfileChanges 直接丢掉
    static bool UpdaterReady(IClientSession session, string profileId)
    {
        try
        {
            var field = AccessTools.Field(session.GetType(), "_profileUpdaters");
            if (field == null || string.IsNullOrEmpty(profileId)) return true;
            return field.GetValue(session) is IDictionary dict && dict.Contains(profileId);
        }
        catch { return true; }
    }

    static IEnumerator Pump()
    {
        var wait = new WaitForSecondsRealtime(1f);
        var waited = 0f;
        while (true)
        {
            yield return wait;
            if (_pending.Count == 0) continue;
            var (backend, quest) = _pending[0];
            if (!QuestOps.IsLobby(backend))
            {
                // 09-26 F1：排队期间进了战局（大厅控制器已释放）——不在战局 / 撤离途中发；回大厅重扫时会重新判
                _pending.RemoveAt(0);
                waited = 0f;
                continue;
            }
            var session = Session() ?? backend.GInterface222_0;
            if (session == null) continue;
            if (!UpdaterReady(session, backend.Profile?.Id))
            {
                waited += 1f;
                if (waited < 60f) continue;
                Plugin.Log.LogWarning("[chain] profile updater still not registered after 60 s; sending anyway (the quest list will be re-fetched afterwards)");
            }
            _pending.RemoveAt(0);
            waited = 0f;
            if (_sent.Contains(quest.Id) || !Failed(quest.QuestStatus) || !ServerStillRunning(quest.Id)) continue;
            _sent.Add(quest.Id);
            IResult done = null;
            var gotCallback = false;
            try { session.QuestFail(quest.Id, r => { done = r; gotCallback = true; }); }
            catch (Exception e)
            {
                _sent.Remove(quest.Id);
                Plugin.Log.LogWarning($"[chain] {quest.Id} QuestFail send failed:{e.Message}");
                continue;
            }
            var t0 = Time.realtimeSinceStartup;
            while (!gotCallback && Time.realtimeSinceStartup - t0 < 30f) yield return null;
            if (!gotCallback) { Plugin.Log.LogWarning($"[chain] {quest.Id} QuestFail got no reply in 30 s; giving up for now (will retry on next login)"); _sent.Remove(quest.Id); continue; }
            if (done != null && done.Failed)
            {
                _sent.Remove(quest.Id);
                Plugin.Log.LogWarning($"[chain] {quest.Id} QuestFail rejected:{done.Error}");
                continue;
            }
            _server[quest.Id] = EQuestStatus.Fail;
            try { backend.ConditionsConnectorsManager.ClearConditions(quest); }
            catch (Exception e) { Plugin.WarnOnce("chain/clearconditions", "[chain] ClearConditions failed after a lobby fail: " + e.Message); }
            if (quest.QuestStatus == EQuestStatus.MarkedAsFailed) quest.SetStatus(EQuestStatus.Fail, notify: true, fromServer: false);
            yield return Refresh(backend, session);
            ChapterEvents.Raise();
        }
    }

    /// 再拉一次任务表，任务书里没有的补进去（QuestBook.AddTemplates 只加缺的；已有的 method_9 会跳过）
    static IEnumerator Refresh(QuestControllerClientBackend backend, IClientSession session)
    {
        if (session is not EftClientBackendSession eft)
        {
            Plugin.Log.LogWarning($"[chain] session is {session.GetType().Name}, not EftClientBackendSession; skipping quest list re-fetch (re-login to see unlocked quests)");
            yield break;
        }
        Task<List<QuestTemplate>> task;
        try { task = eft.RequestQuestsTemplates(false); }
        catch (Exception e) { Plugin.Log.LogWarning("[chain] quest list re-fetch failed: " + e.Message); yield break; }
        var t0 = Time.realtimeSinceStartup;
        while (task != null && !task.IsCompleted && Time.realtimeSinceStartup - t0 < 30f) yield return null;
        if (task == null || !task.IsCompleted || task.IsFaulted || task.Result == null)
        {
            Plugin.Log.LogWarning("[chain] quest list re-fetch returned no result" + (task?.Exception != null ? ": " + task.Exception.GetBaseException().Message : ""));
            yield break;
        }
        var have = new HashSet<string>(backend.Quests.Select(q => q.Id), StringComparer.Ordinal);
        var fresh = task.Result.Where(t => t != null && !string.IsNullOrEmpty(t.Id) && !have.Contains(t.Id)).ToList();
        if (fresh.Count == 0) yield break;
        try
        {
            backend.Quests.AddTemplates(fresh);
        }
        catch (Exception e) { Plugin.Log.LogWarning("[chain] failed to add re-fetched quests to the quest book: " + e.Message); }
    }

    static bool Done(Quest quest, Condition c)
    {
        try { return quest.ProgressCheckers.TryGetValue(c, out var p) && p.HasGetter() && p.Test(); }
        catch { return false; }
    }
}
