using EFT;
using EFT.Quests;
using UnityEngine;
using VisitAPI.Dialog;

namespace VisitAPI.Native;

/// <summary>
/// 单个触发点：距离 + 朝向 + 任务门控 → 弹交互提示/自动起爆 → 接/交/判失败任务或开对话。
/// 判距基准保持 1.2.1 原样（相机位置）——既有 `.dlg` 的 dist 都是按这个调好的，不动（T-2 定案）。
/// 任务状态写入走 Core\QuestOps 唯一出口（T-6）；`once` 参数触发成功后记进 seen.json，跨局不再弹（T-3）。
/// </summary>
public class VisitTrigger : MonoBehaviour
{
    public string TraderId;
    public DialogTrigger Data;
    public bool Merge;
    public bool RequireLook;
    public bool Auto => Data.Auto || Data.Enter >= 0f;

    GamePlayerOwner _owner;
    int _ownerScanAt;         // T-5：找不到 owner 时退避，不再每帧全场景扫
    int _menuSeenFrame = -1;  // T-4：帧序状态归本实例所有，多个 merge 触发点不再互扰
    float _cooldown;
    bool _shown;
    bool _fired;
    bool _onceChecked;        // T-3：once 记号只查一次（要等 MyPlayer 就位才知道档案 id）
    float _armed;
    int _misses;
    float _nearLog;

    // once 记号的键：原文留底最稳——作者改了这一行（哪怕只改提示语）就视为新触发点，重新武装
    string OnceKey => Data.Raw ?? $"{Data.Kind}|{Data.Place}|{Data.X},{Data.Y},{Data.Z}|{Data.Node}";

    static QuestController Quests => QuestOps.Resolve();   // 藏身处选 Backend 版（真事务），战局选 LocalGame 版，见 QuestOps

    void Update()
    {
        if (Data.Once && !_onceChecked && GamePlayerOwner.MyPlayer != null)
        {
            _onceChecked = true;
            if (OnceService.Store(TraderId).TriggerUsed(GamePlayerOwner.MyPlayer.Profile.Id, OnceKey)) { Destroy(gameObject); return; }
        }
        // 对话屏开着（触发对话/访问按钮开的）时触发器整体静默：不弹菜单、不起爆，防止叠开第二屏。
        // 判据是 DialogScreenTracker 的 O(1) 开关——绝不定时/每帧扫场景（坑 #99，T-5 同族病）。
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

    // 走近了但还没够：每 2 秒报一次实际距离。坐标填错/dist 太小是最难自己发现的，
    // 现场什么都不会发生，日志里这一行就是唯一的线索。
    void NearLog()
    {
        if (Camera.main == null || Time.unscaledTime < _nearLog) return;
        var d = Vector3.Distance(Camera.main.transform.position, new Vector3(Data.X, Data.Y, Data.Z));
        if (d > Data.Dist * 5f) return;
        _nearLog = Time.unscaledTime + 2f;
        Plugin.Log.LogInfo($"[trigger] 距触发点 {d:F1}m（需要 ≤{Data.Dist}m）@ ({Data.X}, {Data.Y}, {Data.Z})");
    }

    // T-5：找不到 owner 时每 60 帧扫一次，不再每帧全场景扫；访问用的 NarratePlayerOwner 不算
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
        if (Data.Enter >= 0f) return EnterDue() && GatePasses();
        var main = Camera.main;
        if (main == null) return false;
        var point = new Vector3(Data.X, Data.Y, Data.Z);
        var distance = Vector3.Distance(main.transform.position, point);
        if (distance > Data.Dist) return false;
        if (RequireLook && !LookPasses(main, point)) return false;
        return GatePasses();
    }

    // 进图计时型触发点：起表点是"玩家真正可控"那一刻(MyPlayer 就位)，
    // 不是 GameWorld 生成那一刻——否则读条阶段就把秒数烧完了，落地即触发。
    bool EnterDue()
    {
        if (GamePlayerOwner.MyPlayer == null) return false;
        if (_armed <= 0f) _armed = Time.unscaledTime;
        return Time.unscaledTime - _armed >= Data.Enter;
    }

    // 旧 F11 打的是玩家脚底，比摄像机低约 1.6m（新 F11 已改打相机坐标，T-2），既有 .dlg 两种坐标都在跑。
    // 近距离时这段高度差会把视线角推到远大于视角锥——朝向只比水平分量，高低差交给 dist 距离门槛去挡。
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

    string Prompt() => string.IsNullOrEmpty(Data.Prompt) ? Loc.Pick("对话", "Talk") : Data.Prompt;

    void Fire()
    {
        if (_owner != null) TriggerMenu.Hide(_owner, Prompt());
        _shown = false;
        _cooldown = Time.unscaledTime + 1.5f;
        // accept / finish / fail 可以同时写；三者都没有才去开对话
        var acted = false;
        var handled = true;   // 全部动作都落到实处（任务都找到了/对话开成了）才配打 once 记号
        Plugin.Log.LogInfo($"[trigger] 触发：accept={Data.AcceptId ?? "-"} finish={Data.FinishId ?? "-"} fail={Data.FailId ?? "-"} node={Data.Node ?? "-"}");
        if (Data.AcceptId != null) { handled &= AcceptQuest(); acted = true; }
        if (Data.FinishId != null) { handled &= SetStatus(Data.FinishId, EQuestStatus.Success); acted = true; }
        if (Data.FailId != null) { handled &= SetStatus(Data.FailId, EQuestStatus.Fail); acted = true; }
        if (!acted)
        {
            // 起爆瞬间复查：对话屏已开就放弃这次，回炉重试
            if (DialogScreenTracker.Open)
            { Plugin.Log.LogInfo("[trigger] 对话屏开着，这次不弹（冷却后重试）"); handled = false; _fired = false; }
            else
            {
                var tree = DialogFiles.Loader.Load(TraderId);
                if (tree == null) { Plugin.Log.LogWarning("[trigger] no .dlg for " + TraderId); handled = false; }
                else if (!DialogOpener.TryOpenTriggered(tree, Data.Node, out var error))
                { Plugin.Log.LogWarning("[trigger] open failed: " + error); handled = false; }
            }
        }
        if (handled && Data.Once) MarkOnce();
    }

    // T-3：触发成功才记号；记完自毁，本局也不再弹
    void MarkOnce()
    {
        var player = GamePlayerOwner.MyPlayer;
        if (player == null) return;
        OnceService.Store(TraderId).MarkTrigger(player.Profile.Id, OnceKey);
        Plugin.Log.LogInfo("[trigger] once 记号已打，这个触发点不会再弹");
        Destroy(gameObject);
    }

    // 战局内 LocalGame 版的 AcceptQuest 就是本地 SetConditionalStatus(Started)（SPT 结算时回写）；
    // 藏身处经 QuestOps.Resolve 拿到的 Backend 版才是发服务端的真事务。返回「任务找到了」。
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

    // 走到/进图就把某条任务判成完成或失败（剧情用）。没接过的任务先接下再改，否则引擎不认这个状态迁移。返回「任务找到了」。
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
        if (quest.QuestStatus == EQuestStatus.AvailableForStart) quests.SetConditionalStatus(quest, EQuestStatus.Started);   // 没接过的先接下
        if (want == EQuestStatus.Success)
        {
            // ⚠️ 引擎不认 Started → Success 这一跳（实机实证）。必须先落到「可提交」，
            // 再走 QuestOps.Finish —— 那才是真交任务：发奖励、发邮件、同步服务端。
            // 上面那次 SetConditionalStatus 会同步惊动 ChapterChain：若它抢先对同一条任务发了 finish，
            // QuestOps 的在途表会把这里的重复发起吃掉——只交一次（T-6 的双交病根就在这）。
            if (quest.QuestStatus < EQuestStatus.AvailableForFinish) quests.SetConditionalStatus(quest, EQuestStatus.AvailableForFinish);
            QuestOps.Finish(quests, quest, "trigger");
            return true;
        }
        if (QuestOps.SetStatus(quests, quest, want, "trigger")) Plugin.Log.LogInfo($"[trigger] {questId} -> {want}（实际变成 {quest.QuestStatus}）");
        return true;
    }

    // 玩家/任务书可能还没就绪(战局刚载入)，别把 auto 点永久锁死——退回去等冷却后重试；20 次还没有就是 id 写错/前置没到，别刷屏
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
