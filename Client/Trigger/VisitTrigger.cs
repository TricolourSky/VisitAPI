using System;
using System.Linq;
using EFT;
using EFT.Hideout;
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
    Action _fire;   // Fire 的委托只建一次：每帧传方法组会每帧 new 一个 Action（10-29）
    int _ownerScanAt;
    int _menuSeenFrame = -1;
    float _cooldown;
    bool _shown;
    bool _fired;
    bool _onceChecked;
    float _armed;
    int _misses;
    bool _onceAtClose;
    bool _burned;
    bool _sawDialogOpen;
    float _onceDeadline;
    float _closedAt = -1f;
    // 10-24：地点解析成的藏身处区域、当前语言的提示语都只算一次
    bool _areaParsed;
    EAreaType? _area;
    string _prompt, _promptLang;

    internal string Key => KeyOf(Data);
    internal static string KeyOf(DialogTrigger d) => d.Raw ?? $"{d.Kind}|{d.Place}|{d.X},{d.Y},{d.Z}|{d.Node}";
    /// 10-15：聊天「回复」把玩家传到线前以后直接按下（InviteHideout），不等距离 / 朝向检查；once / 门 / 节点照 Fire 原样
    internal void FireNow() => Fire();

    static QuestController Quests => QuestOps.Resolve();

    void Update()
    {
        if (Data.Once && !_onceChecked && GamePlayerOwner.MyPlayer != null)
        {
            _onceChecked = true;
            if (OnceService.Used(GamePlayerOwner.MyPlayer.Profile, OnceService.TriggerId(TraderId, Key)))
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
            // 10-01：自动线等战局正式开始才动作，enter 的秒数也从这一刻起算。从 MyPlayer 就位（还在加载 / 倒计时）起表的话，
            // 到点时战局控制器还不算活（QuestOps.IsLive），接 / 交被挡掉而 _fired 已置位，这一局再也不触发（SORA 立交桥：加载 50 秒 > enter 45）
            if (!RaidStarted()) return;
            if (!_fired && GamePlayerOwner.MyPlayer != null && Time.unscaledTime >= _cooldown && ShouldShow())
            {
                _fired = true;
                Fire();
            }
            return;
        }
        if (_owner == null && !FindOwner()) return;
        if (Time.unscaledTime < _cooldown) return;
        if (ShouldShow()) _shown = TriggerMenu.Show(_owner, Prompt(), _fire ??= Fire, Merge, ref _menuSeenFrame);
        else if (_shown) { TriggerMenu.Hide(_owner, Prompt()); _shown = false; }
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
        var inArea = InHideoutArea();
        if (inArea.HasValue) return inArea.Value && GatePasses() && BurnedAllows();
        var main = Camera.main;
        if (main == null) return false;
        var point = new Vector3(Data.X, Data.Y, Data.Z);
        var distance = Vector3.Distance(main.transform.position, point);
        if (distance > Data.Dist) return false;
        if (RequireLook && !LookPasses(main, point)) return false;
        return GatePasses() && BurnedAllows();
    }

    /// 10-19 SORA：「坐标直接内嵌进入安保和休息区的菜单里就行了，不需要自定义坐标，和情报中心一样」——
    /// 藏身处的线（不是 free 的）地点写的是区域名（Security / RestSpace / IntelligenceCenter…）时按区域判：
    /// 屏幕上的原生菜单是这块区域的，就并进去；坐标不看，只剩聊天「回复」把人传过去时用。
    /// 地点不是区域名照旧按坐标判。返回 null = 这条线不按区域判
    bool? InHideoutArea()
    {
        if (!Merge) return null;
        if (!_areaParsed) { _areaParsed = true; _area = Enum.TryParse(Data.Place, true, out EAreaType want) ? want : (EAreaType?)null; }
        if (_area == null) return null;
        if (_owner is not HideoutPlayerOwner hideout) return false;
        return MenuArea(hideout)?.Data?.Template?.Type == _area.Value;
    }

    /// <summary>屏幕上的原生菜单属于哪块区域（不是区域菜单就给 null）。先后照 HideoutPlayerOwner.InteractionsChangedHandler：
    /// 准星对着的物件 → 放物品的区 → 撤离点 → 人站进的那块区域；它判空用的是引用比较，这里也是。
    /// 10-25 SORA：「玩家站的和情报中心太近就看不见了」——藏身处场景（level161）里每块区域有两个代表它的碰撞体：
    /// 桌子前面那片互动范围（*_area_trigger，走进去设 HideoutArea）和桌子本身（*_raycast_collider，准星一米多以内对上就是 InteractableObject），
    /// 两个都是 HideoutInteractionTrigger（IHideoutAreaWrapper），出的是同一份区域菜单。只认前一种的话，准星一对上桌子就不并了。
    /// 准星对着别的区域（情报中心旁边紧挨着比特币矿机）或别的物件时，菜单已经是别人的，不并</summary>
    static HideoutArea MenuArea(HideoutPlayerOwner hideout)
    {
        var player = hideout.Player;
        if (player is null) return null;
        var looked = player.InteractableObject;
        if (looked is not null) return (looked as IHideoutAreaWrapper)?.Area;
        if (player.PlaceItemZone is not null || player.ExfiltrationPoint is not null) return null;
        return hideout.HideoutArea;
    }

    /// 藏身处的线不看：那里没有开局倒计时，任务走大厅那份控制器
    bool RaidStarted() => Data.Kind != "raid" || Raid.Started;

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

    /// 触发点的门：写了 if 就必须写 statuses，没写等于永远不出现（SORA 10-17：「这个得写，不然玩家可以跳过某个阶段直接互动」）——和区域 / 刷兵那套 QuestOps.StatusIn 不同
    bool GatePasses()
    {
        if (Data.IfQuestId == null) return true;
        Quest quest = Quests?.Quests?.GetConditional(Data.IfQuestId);
        return quest != null && Data.IfStatuses.Contains((int)quest.QuestStatus);
    }

    /// <summary>提示语按玩家语言取（.dlg 里 trigger: 行下面的译文行，Loc.Tr：没有这门语言退英文再退原文）；没写就是「对话」。Key 仍按原行记，切语言不会多触发一次。</summary>
    string Prompt()
    {
        var lang = Loc.Code;
        if (_prompt == null || _promptLang != lang)
        {
            _promptLang = lang;
            var p = Loc.Tr(Data.Tr, Data.Prompt);
            _prompt = string.IsNullOrEmpty(p) ? Loc.Pick("对话", "Talk") : p;
        }
        return _prompt;
    }

    void Fire()
    {
        if (_owner != null) TriggerMenu.Hide(_owner, Prompt());
        _shown = false;
        _cooldown = Time.unscaledTime + 1.5f;
        var acted = false;
        var handled = true;
        if (Voice != null)
        {
            var pick = Voice.Pick();
            if (!string.IsNullOrEmpty(pick.Audio)) RaidVoice.PlayAt(pick.Audio, VoiceAt, Voice.Volume);
            RaidSubtitles.Play(pick.Lines, "interaction trigger " + TraderId);
            acted = true;
        }
        if (ZoneToComplete != null)
        {
            try
            {
                var player = Raid.MainPlayer;
                if (player != null) ZoneToComplete.TriggerEnter(player);
                else Plugin.Log.LogWarning("[trigger] no main player; quest zone not triggered");
            }
            catch (Exception e) { Plugin.Log.LogWarning("[trigger] manual quest zone trigger failed: " + e.Message); }
        }
        if (Data.AcceptId != null) { handled &= AcceptQuest(); acted = true; }
        if (Data.FinishId != null) { handled &= SetStatus(Data.FinishId, EQuestStatus.Success); acted = true; }
        if (Data.FailId != null) { handled &= SetStatus(Data.FailId, EQuestStatus.Fail); acted = true; }
        var openedDialog = false;
        if (!acted)
        {
            if (DialogScreenTracker.Open)
            { handled = false; _fired = false; }
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
            Plugin.Log.LogWarning($"[trigger] triggered dialog screen did not appear within 10 s; once not recorded{(Auto ? ", this auto trigger won't fire again this raid" : ", prompt will reappear after cooldown")}");
            if (!Auto) _cooldown = Time.unscaledTime + 1.5f;
            return false;
        }
        if (_closedAt < 0f) _closedAt = Time.unscaledTime;
        if (Time.unscaledTime - _closedAt < 2f) return true;
        ResetOnce();
        if (DialogLeftGateUnresolved())
        {
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
            return true;
        }
        Destroy(gameObject);
        return false;
    }

    bool OpensDialog => Voice == null && Data.AcceptId == null && Data.FinishId == null && Data.FailId == null;

    /// 这条线开的对话里，从入口走得到的选项里还有没有「此刻点下去就能推进任务」的（接 / 交 / 上交 / 改状态）：有就是对话没谈完，once 不记。
    /// 10-24：遍历走 VisitReach.Walk（和金色电话那套同一份），顺带也认抉择窗「否」的去处和跨剧本跳转
    bool DialogLeftGateUnresolved()
    {
        if (!OpensDialog || !GatePasses() || Quests?.Quests == null) return false;
        var tree = DialogFiles.Loader.Load(TraderId);
        if (tree == null) return false;
        var entries = Data.Node != null ? new[] { Data.Node } : new[] { tree.First, "@start" };
        foreach (var (_, node) in VisitReach.Walk(tree, entries, VisitReach.ById(DialogFiles.All())))
            if (node.Options.Any(CanAdvance)) return true;
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
        if (ss != null && Enum.IsDefined(typeof(EQuestStatus), o.SetStatusValue) && (int)ss.QuestStatus != o.SetStatusValue) return true;
        return false;
    }

    void MarkOnce()
    {
        var player = GamePlayerOwner.MyPlayer;
        if (player == null) return;
        OnceService.Mark(player.Profile, OnceService.TriggerId(TraderId, Key));
        Destroy(gameObject);
    }

    bool AcceptQuest()
    {
        var quests = Quests;
        Quest quest = quests?.Quests?.GetConditional(Data.AcceptId);
        if (quest == null) return Miss(Data.AcceptId);
        if (quest.QuestStatus != EQuestStatus.AvailableForStart) return true;
        QuestOps.Accept(quests, quest, "trigger");
        return true;
    }

    bool SetStatus(string questId, EQuestStatus want)
    {
        var quests = Quests;
        Quest quest = quests?.Quests?.GetConditional(questId);
        if (quest == null) return Miss(questId);
        if (quest.QuestStatus == want || quest.QuestStatus is EQuestStatus.Success or EQuestStatus.Fail or EQuestStatus.MarkedAsFailed) return true;
        if (quest.QuestStatus == EQuestStatus.AvailableForStart) quests.SetConditionalStatus(quest, EQuestStatus.Started);
        if (want == EQuestStatus.Success)
        {
            if (quest.QuestStatus < EQuestStatus.AvailableForFinish) quests.SetConditionalStatus(quest, EQuestStatus.AvailableForFinish);
            QuestOps.Finish(quests, quest, "trigger");
            return true;
        }
        QuestOps.SetStatus(quests, quest, want, "trigger");
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
