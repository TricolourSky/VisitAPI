using System.Collections.Generic;
using EFT;
using EFT.Quests;
using UnityEngine;
using VisitAPI.Dialog;

namespace VisitAPI.Native;

public class VisitTrigger : MonoBehaviour
{
    public string TraderId;
    public DialogTrigger Data;
    public bool Merge;
    public bool RequireLook;
    public QuestZones.Subtitle Voice;
    public Vector3 VoiceAt;
    public EFT.Interactive.TriggerWithId ZoneToComplete;
    public bool Auto => Data.Auto || Data.Enter >= 0f;

    GamePlayerOwner _owner;
    int _ownerScanAt;
    int _menuSeenFrame = -1;
    float _cooldown;
    bool _shown;
    bool _fired;
    bool _onceChecked;
    float _armed;
    int _misses;
    float _nearLog;
    bool _onceAtClose;
    bool _burned;
    bool _sawDialogOpen;
    float _onceDeadline;
    float _closedAt = -1f;

    string OnceKey => Data.Raw ?? $"{Data.Kind}|{Data.Place}|{Data.X},{Data.Y},{Data.Z}|{Data.Node}";

    static QuestController Quests => QuestOps.Resolve();

    void Update()
    {
        if (Data.Once && !_onceChecked && GamePlayerOwner.MyPlayer != null)
        {
            _onceChecked = true;
            if (OnceService.Used(GamePlayerOwner.MyPlayer.Profile, OnceService.TriggerId(TraderId, OnceKey)))
            {
                if (!OpensDialog) { Destroy(gameObject); return; }
                _burned = true;
            }
        }
        if (_onceAtClose && PendingOnce()) return;
        if (Narrating.Now || DialogScreenTracker.Open)
        {
            if (_shown && _owner != null) { TriggerMenu.Hide(_owner, Prompt()); _shown = false; }
            return;
        }
        if (Auto)
        {
            if (!_fired && Data.Enter < 0f) NearLog();
            if (!_fired && GamePlayerOwner.MyPlayer != null && Time.unscaledTime >= _cooldown && ShouldShow())
            {
                _fired = true;
                Fire();
            }
            return;
        }
        if (_owner == null && !FindOwner()) return;
        if (Time.unscaledTime < _cooldown) return;
        if (ShouldShow()) _shown = TriggerMenu.Show(_owner, Prompt(), Fire, Merge, ref _menuSeenFrame);
        else if (_shown) { TriggerMenu.Hide(_owner, Prompt()); _shown = false; }
    }

    void NearLog()
    {
        if (Camera.main == null || Time.unscaledTime < _nearLog) return;
        var d = Vector3.Distance(Camera.main.transform.position, new Vector3(Data.X, Data.Y, Data.Z));
        if (d > Data.Dist * 5f) return;
        _nearLog = Time.unscaledTime + 2f;
        Plugin.Log.LogInfo($"[trigger] 距触发点 {d:F1}m（需要 ≤{Data.Dist}m）@ ({Data.X}, {Data.Y}, {Data.Z})");
    }

    bool FindOwner()
    {
        if (Time.frameCount < _ownerScanAt) return false;
        _ownerScanAt = Time.frameCount + 60;
        foreach (var o in FindObjectsOfType<GamePlayerOwner>())
            if (!(o is NarratePlayerOwner)) { _owner = o; break; }
        return _owner != null;
    }

    bool ShouldShow()
    {
        if (Data.Enter >= 0f) return EnterDue() && GatePasses() && BurnedAllows();
        var main = Camera.main;
        if (main == null) return false;
        var point = new Vector3(Data.X, Data.Y, Data.Z);
        var distance = Vector3.Distance(main.transform.position, point);
        if (distance > Data.Dist) return false;
        if (RequireLook && !LookPasses(main, point)) return false;
        return GatePasses() && BurnedAllows();
    }

    bool EnterDue()
    {
        if (GamePlayerOwner.MyPlayer == null) return false;
        if (_armed <= 0f) _armed = Time.unscaledTime;
        return Time.unscaledTime - _armed >= Data.Enter;
    }

    bool LookPasses(Camera cam, Vector3 point)
    {
        var flat = point - cam.transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.04f) return true;
        var forward = cam.transform.forward;
        forward.y = 0f;
        return Vector3.Angle(forward, flat) <= Mathf.Clamp(Mathf.Atan2(Data.Radius, Mathf.Max(flat.magnitude, 0.5f)) * Mathf.Rad2Deg, 8f, 40f);
    }

    bool GatePasses()
    {
        if (Data.IfQuestId == null) return true;
        Quest quest = Quests?.Quests?.GetConditional(Data.IfQuestId);
        return quest != null && Data.IfStatuses.Contains((int)quest.QuestStatus);
    }

    /// <summary>提示语按玩家语言取（.dlg 里 trigger: 行下面的译文行，DialogLangs）；没写就是「对话」。OnceKey 仍按原行记，切语言不会多触发一次。</summary>
    string Prompt() { var p = DialogLangs.Pick(Data.Tr, Loc.Code, Data.Prompt); return string.IsNullOrEmpty(p) ? Loc.Pick("对话", "Talk") : p; }

    void Fire()
    {
        if (_owner != null) TriggerMenu.Hide(_owner, Prompt());
        _shown = false;
        _cooldown = Time.unscaledTime + 1.5f;
        var acted = false;
        var handled = true;
        Plugin.Log.LogInfo($"[trigger] 触发：accept={Data.AcceptId ?? "-"} finish={Data.FinishId ?? "-"} fail={Data.FailId ?? "-"} node={Data.Node ?? "-"}{(Voice != null ? $" voice(池 {Voice.Pool.Count})" : "")}");
        if (Voice != null)
        {
            var pick = Voice.Pick();
            if (!string.IsNullOrEmpty(pick.Audio)) RaidVoice.PlayAt(pick.Audio, VoiceAt, Voice.Volume);
            RaidSubtitles.Play(pick.Lines, "交互触发点 " + TraderId);
            acted = true;
        }
        if (ZoneToComplete != null)
        {
            try
            {
                var player = Comfort.Common.Singleton<GameWorld>.Instantiated ? Comfort.Common.Singleton<GameWorld>.Instance.MainPlayer : null;
                if (player != null) { ZoneToComplete.TriggerEnter(player); Plugin.Log.LogInfo($"[trigger] 按下交互，手动触发任务区域 {ZoneToComplete.Id}（到达）"); }
                else Plugin.Log.LogWarning("[trigger] 主玩家不在，任务区域没触发");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("[trigger] 手动触发任务区域失败: " + e.Message); }
        }
        if (Data.AcceptId != null) { handled &= AcceptQuest(); acted = true; }
        if (Data.FinishId != null) { handled &= SetStatus(Data.FinishId, EQuestStatus.Success); acted = true; }
        if (Data.FailId != null) { handled &= SetStatus(Data.FailId, EQuestStatus.Fail); acted = true; }
        var openedDialog = false;
        if (!acted)
        {
            if (DialogScreenTracker.Open)
            { Plugin.Log.LogInfo("[trigger] 对话屏开着，这次不弹（冷却后重试）"); handled = false; _fired = false; }
            else
            {
                var tree = DialogFiles.Loader.Load(TraderId);
                if (tree == null) { Plugin.Log.LogWarning("[trigger] no .dlg for " + TraderId); handled = false; }
                else if (!DialogOpener.TryOpenTriggered(tree, Data.Node, out var error))
                { Plugin.Log.LogWarning("[trigger] open failed: " + error); handled = false; }
                else openedDialog = true;
            }
        }
        if (handled && Data.Once)
        {
            if (openedDialog) { _onceAtClose = true; _sawDialogOpen = false; _closedAt = -1f; _onceDeadline = Time.unscaledTime + 10f; }
            else MarkOnce();
        }
    }

    bool PendingOnce()
    {
        if (DialogScreenTracker.Open) { _sawDialogOpen = true; _closedAt = -1f; return true; }
        if (!_sawDialogOpen)
        {
            if (Time.unscaledTime < _onceDeadline) return true;
            ResetOnce();
            Plugin.Log.LogWarning($"[trigger] 触发的对话屏 10 秒内没出现，once 不记{(Auto ? "，这个自动触发点本局不再弹" : "，冷却后重新出提示")}");
            if (!Auto) _cooldown = Time.unscaledTime + 1.5f;
            return false;
        }
        if (_closedAt < 0f) _closedAt = Time.unscaledTime;
        if (Time.unscaledTime - _closedAt < 2f) return true;
        ResetOnce();
        if (DialogLeftGateUnresolved())
        {
            Plugin.Log.LogInfo($"[trigger] 对话关了，但它还能推进的任务都没推进（节点 {Data.Node ?? "入口"}）——当成没走完，once 不记{(Auto ? "，下次进图再弹" : "，走过去还能再对话")}");
            if (!Auto) _cooldown = Time.unscaledTime + 1.5f;
            return false;
        }
        MarkOnce();
        return true;
    }

    void ResetOnce() { _onceAtClose = false; _sawDialogOpen = false; _closedAt = -1f; }

    bool BurnedAllows()
    {
        if (!_burned) return true;
        if (DialogLeftGateUnresolved())
        {
            _burned = false;
            Plugin.Log.LogInfo($"[trigger] once 记号已打，但这段对话（节点 {Data.Node ?? "入口"}）还推进得了剧情——按「上次没走完」处理，这次照常可用");
            return true;
        }
        Destroy(gameObject);
        return false;
    }

    bool OpensDialog => Voice == null && Data.AcceptId == null && Data.FinishId == null && Data.FailId == null;

    bool DialogLeftGateUnresolved()
    {
        if (!OpensDialog) return false;
        if (Data.IfQuestId != null && !GatePasses()) return false;
        if (Quests?.Quests == null) return false;
        var tree = DialogFiles.Loader.Load(TraderId);
        if (tree == null) return false;
        var queue = new Queue<string>();
        void Enqueue(string t)
        {
            if (string.IsNullOrEmpty(t)) return;
            if (t == "@start") { Enqueue(tree.Start); foreach (var w in tree.WhenRules) Enqueue(w.Node); return; }
            if (t[0] == '@') return;
            queue.Enqueue(t);
        }
        if (Data.Node != null) Enqueue(Data.Node);
        else { Enqueue(tree.First); Enqueue("@start"); }
        var seen = new HashSet<string>();
        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            if (!seen.Add(name) || !tree.Nodes.TryGetValue(name, out var node)) continue;
            Enqueue(node.JumpTo);
            foreach (var o in node.Options)
            {
                if (CanAdvance(o)) return true;
                Enqueue(o.Target);
            }
        }
        return false;
    }

    Quest Q(string id) => string.IsNullOrEmpty(id) ? null : Quests?.Quests?.GetConditional(id);

    bool CanAdvance(DialogOption o)
    {
        var explicitGate = o.Always || o.IfQuestId != null || o.IfNotQuestId != null || o.IfVarName != null;
        foreach (var id in o.CompleteIds)
        {
            var s = Q(id)?.QuestStatus;
            if (s == EQuestStatus.AvailableForFinish || (explicitGate && s == EQuestStatus.Started)) return true;
        }
        foreach (var id in o.AcceptIds)
            if (Q(id)?.QuestStatus == EQuestStatus.AvailableForStart) return true;
        var h = Q(o.HandoverId);
        if (h != null && h.QuestStatus == EQuestStatus.Started && QuestGates.PendingItems(h) != null) return true;
        var ss = Q(o.SetStatusId);
        if (ss != null && System.Enum.IsDefined(typeof(EQuestStatus), o.SetStatusValue) && (int)ss.QuestStatus != o.SetStatusValue) return true;
        return false;
    }

    void MarkOnce()
    {
        var player = GamePlayerOwner.MyPlayer;
        if (player == null) return;
        OnceService.Mark(player.Profile, OnceService.TriggerId(TraderId, OnceKey), "trigger");
        Plugin.Log.LogInfo("[trigger] once 记号已打（档案变量），这个触发点不会再弹");
        Destroy(gameObject);
    }

    bool AcceptQuest()
    {
        var quests = Quests;
        Quest quest = quests?.Quests?.GetConditional(Data.AcceptId);
        if (quest == null) return Miss(Data.AcceptId);
        if (quest.QuestStatus != EQuestStatus.AvailableForStart)
        {
            Plugin.Log.LogInfo($"[trigger] {Data.AcceptId} 现在是 {quest.QuestStatus}，不是「可接」，没接");
            return true;
        }
        QuestOps.Accept(quests, quest, "trigger");
        return true;
    }

    bool SetStatus(string questId, EQuestStatus want)
    {
        var quests = Quests;
        Quest quest = quests?.Quests?.GetConditional(questId);
        if (quest == null) return Miss(questId);
        if (quest.QuestStatus == want || quest.QuestStatus == EQuestStatus.Success || quest.QuestStatus == EQuestStatus.Fail || quest.QuestStatus == EQuestStatus.MarkedAsFailed)
        {
            Plugin.Log.LogInfo($"[trigger] {questId} 已经是 {quest.QuestStatus}，不动它");
            return true;
        }
        if (quest.QuestStatus == EQuestStatus.AvailableForStart) quests.SetConditionalStatus(quest, EQuestStatus.Started);
        if (want == EQuestStatus.Success)
        {
            if (quest.QuestStatus < EQuestStatus.AvailableForFinish) quests.SetConditionalStatus(quest, EQuestStatus.AvailableForFinish);
            QuestOps.Finish(quests, quest, "trigger");
            return true;
        }
        if (QuestOps.SetStatus(quests, quest, want, "trigger")) Plugin.Log.LogInfo($"[trigger] {questId} -> {want}（实际变成 {quest.QuestStatus}）");
        return true;
    }

    bool Miss(string questId)
    {
        if (++_misses <= 20) _fired = false;
        if (_misses == 1 || _misses == 20) Plugin.Log.LogWarning("[trigger] quest not found: " + questId + (_misses == 20 ? " (giving up)" : ""));
        return false;
    }

    void OnDestroy()
    {
        if (_shown && _owner != null) TriggerMenu.Hide(_owner, Prompt());
    }
}
