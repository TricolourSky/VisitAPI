using System;
using System.Collections.Generic;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;
using UnityEngine.AI;

namespace VisitAPI.Native;

/// <summary>10-16 护卫的跟随层。10-25 起由插件自己托管（BotLayers，不再借 BigBrain），什么兵种都能挂。
/// 优先级 72：压过原版的日常交火层（PMC 60、远敌 61、警告 65、请求 70）和 SAIN 的交火 20 / 22、撤离 24；
/// 让给排除卡壳 73 / 78、躲雷 80（Rogue 100）、闪光 85。原版里比它高、对护卫又添乱的几层（PMC 的撤离 79、警告玩家、Rogue 的和平请求、Scav 离图）
/// 对护卫关掉（EscortGuards.NativeLayers）。让位以后接手打的是兵自己的 AI：装了 SAIN 就是 SAIN，没装就是这个兵种的原版大脑。
/// 10-17（SORA 街区实机：四只全跑到 150 米外的药房去了）：SAIN 把它选的敌人（听见的、队友看见的都算）写进原版 GoalEnemy，
/// 「没敌人才跟随」在满图是兵的地方几乎永远不成立。所以：① 只有自己 5 秒内亲眼看见、50 米内的敌人或正被压制才让位；
/// ② 离主人 45 米强制回来（回到 20 米内才算）；③ 卡住 / 掉队先强制重算路、再不行传送到主人身后视野外（10-29：走路时每帧喂门，关着的门自己开）；④ SAIN 撤离层对护卫不激活（EscortNoExtract）。
/// 10-17（SORA：「更听话了，但丢了原有的逻辑，状态差了不会打药补血」「有时候走路会原地踏步」）：
/// ⑤ 顾自己：SAIN 的打药 / 止血 / 手术 / 打针 / 换弹只在它自己的层激活时执行（SAINSelfActionClass 开头就判 SAINLayersActive），
///    跟随层接管了平时，这些就全没了——照 SAIN 的调用（原版 BotMedecine / BotReload）在跟随时补上；手术站定，其余边走边做、不冲刺。
/// ⑥ 原地踏步：阵型跟主人的前进方向走（不跟视线绕圈）；位子从主人脚下沿导航网格拉直线，隔墙就退成纵队；到位就停、目标挪出 3 米才重新走（滞回）；
///    冲刺 14 米开、7 米关；走着却 2.5 秒没挪窝先强制重算路，再 2.5 秒还没动就原地停下，等主人走开再说；别的护卫已占的位子不往里挤。
/// 指令：跟随（阵型）/ 待命 / 集合 / 冲。</summary>
public static class EscortBrain
{
    public const int Priority = 72;
    /// 突入层：压过原版的远敌 59 / 61、压制推进 58、PMC 60；让给请求 65 / 70、求援 70 / 75、卡壳、躲雷
    public const int RushPriority = 64;

    /// <summary>跟随层 / 突入层共用的走路底座：
    /// 起手站起来、端枪、收冲刺；停下时收冲刺；冲刺开关带滞回（已经在冲就看 off 线，没冲就看 on 线），阈值由子类给。</summary>
    public abstract class MoveLogic : VisitLogic
    {
        protected bool Sprinting;   // 想不想冲；实际冲没冲看 Mover.Sprinting
        bool _sprintAsked;
        float _sprintAskedAt = -10f;

        protected MoveLogic(BotOwner bot) : base(bot) { }

        public override void Start()
        {
            Sprinting = false;
            EscortSain.ReleaseLayer(BotOwner);   // SAIN 打完把原版走路器还回来，不然我们发的路没人走（10-26）
            try { BotOwner.Mover?.Sprint(false); BotOwner.BotLay?.GetUp(true); BotOwner.SetPose(1f); } catch { }
            // 「在门边」记号只有原版的走路节点每帧刷新（MoveNode.DoorOpen），我们的节点不刷：上一层留下的「在门边」会让原版每帧把速度压到一半（10-26）
            if (BotOwner.DoorOpener != null) BotOwner.DoorOpener.NearDoor = false;
        }

        public override void Stop()
        {
            try { BotOwner.Mover?.Sprint(false); } catch { }
            Sprinting = false;
        }

        /// 冲刺以 Mover 上的实际状态为准（10-26 SORA 实机：「结果打完人了之后又开始慢慢走了。我跑起来也不跑」）：SAIN 换掉了 BotMover.Sprint，
        /// 它的交火记号还开着（接手那一刻往往还没清）、或者兵没对着当前拐点（差 20° 以上），就不理这次请求；
        /// Mover 和想法对不上就再发（同样的请求 0.25 秒一次）；走原版的冲刺入口，要冲时先站直、放下瞄准
        protected void SetSprint(bool on)
        {
            Sprinting = on;
            if (BotOwner.Mover.Sprinting == on || on == _sprintAsked && Time.time - _sprintAskedAt < 0.25f) return;
            _sprintAsked = on; _sprintAskedAt = Time.time;
            BotOwner.Sprint(on);
        }

        protected bool WantSprint(float distance, float on, float off) => Sprinting ? distance > off : distance > on;

        /// 走着的时候脸朝前、站着走。Mover 只管路不管朝向和姿态：上一层（SAIN / 原版，或者自己站定时的「跟主人看同一个方向」）
        /// 留下的盯点和蹲姿会一直带着走（10-25 夜实机：蹲着倒退跟人）
        protected void FaceTravel()
        {
            BotOwner.Steering.LookToMovingDirection();
            BotOwner.SetPose(1f);
        }

        protected bool PassingDoor;    // Mover 在 NearDoor 态：这一段路由原版 DoorOpener 自己发（走到门把手前、开门、穿过去），不发新路、不算卡住
        protected bool DoorBlocksRun;  // 五米内有关着的门：不冲刺（原版走路节点同样只在 CanRun 时冲）

        /// 10-29：原版只在走路节点每帧调 MoveNode.DoorOpen 时才开门——BotDoorOpener 只在 Mover 的 NearDoor 态工作，进入该态只经
        /// DoorOpen → UpdateDoorInteractionStatus → TryPassCurrentDoor（反编译核过）。我们的节点以前不调它，护卫卡在关着的门前等传送。
        /// 走路的每一帧喂一次；不扫雷（躲雷有它自己的层）
        protected void DoorStep()
        {
            try
            {
                var s = Node != null ? Node.DoorOpen(false) : DoorInteractionStatus.CanRun;
                DoorBlocksRun = s != DoorInteractionStatus.CanRun;
                PassingDoor = BotOwner.Mover.CurrentState == EBotMoverState.NearDoor;
            }
            catch (Exception e)
            {
                PassingDoor = DoorBlocksRun = false;
                Plugin.WarnOnce("escort-door", "[escort] door handling failed; escorts will not open doors by themselves: " + e.Message);
            }
        }
    }

    /// <summary>10-17 SORA「看门狗」：剧情刷出来冲着玩家去的兵（StoryRush 名单）。没看见人之前一路往玩家所在的位置压（「突入进房间」），
    /// 自己 5 秒内亲眼看见、35 米内的玩家或正被压制 → 让给兵自己的 AI 打（SAIN 或原版）；玩家躲起来 5 秒没再看见，就接着往他那里压。只插给突入兵（BotLayers）。</summary>
    public class RushLayer : VisitLayer
    {
        public RushLayer(BotOwner bot, int priority) : base(bot, priority) { }

        protected override string LayerName => "VisitAPI Rush";

        protected override Type Logic => typeof(RushLogic);

        protected internal override VisitLogic NewLogic() => new RushLogic(BotOwner);

        protected override bool Active()
        {
            var t = StoryRush.TargetOf(BotOwner);
            if (!Raid.Alive(t)) return false;
            var mem = BotOwner.Memory;
            if (mem == null || mem.IsUnderFire) return false;
            return !Fighting(BotOwner, 35f);
        }
    }

    public class RushLogic : MoveLogic
    {
        float _nextPath;

        public RushLogic(BotOwner bot) : base(bot) { }

        public override void Start()
        {
            base.Start();
            _nextPath = 0f;
            try { BotOwner.SetTargetMoveSpeed(1f); } catch { }
        }

        public override void Update()
        {
            var t = StoryRush.TargetOf(BotOwner);
            if (t == null) return;
            try
            {
                var d = (t.Position - BotOwner.Position).magnitude;
                DoorStep();
                if (Time.time > _nextPath && !PassingDoor)
                {
                    _nextPath = Time.time + 1f;
                    if (BotOwner.GoToPoint(t.Position, true, 1.5f) != NavMeshPathStatus.PathComplete)
                        BotOwner.GoToPoint(t.Position, true, 1.5f, false, false);
                }
                var sprint = !DoorBlocksRun && WantSprint(d, 15f, 9f);   // 远了冲刺，近了端枪走（滞回，不来回切）；门前不冲
                SetSprint(sprint);
                if (sprint) FaceTravel();
                else { BotOwner.SetPose(1f); BotOwner.Steering.LookToPoint(t.Position + Vector3.up * 1.4f); }   // 进门那一眼就朝着人
            }
            catch (Exception e) { Plugin.WarnOnce("rush-step", "[spawns] rush step failed, attackers may stop pushing: " + e.Message); }
        }
    }

    const float Leash = 45f;       // 离主人超过这个距离：不管 SAIN 在干什么都先回来（正被压制除外）
    const float LeashDone = 20f;   // 回到这个距离以内才算回来了
    const float FightSeen = 5f;    // 敌人最后一次亲眼看见距今不到这个秒数才算「真在打」
    const float FightRange = 50f;  // 敌人离兵超过这个距离不算「真在打」（远处晃一下的人影不追）

    static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);
    static float Far(BotOwner bot, Player owner) => Flat(owner.Position - bot.Position).magnitude;

    /// 「真在打」：SAIN 给的 GoalEnemy 太宽（听见的、队友看见的都算），只认自己最近亲眼看见、而且不远的
    public static bool Fighting(BotOwner bot, float range = FightRange)
    {
        var e = bot?.Memory?.GoalEnemy;
        if (e == null) return false;
        try { return (e.IsVisible || Time.time - e.PersonalLastSeenTime < FightSeen) && e.Distance < range; }
        catch { return true; }
    }

    /// 「冲」往哪冲（10-26 SORA 选 B）：自己有目标冲自己的；自己没有（装了 SAIN，没亲眼发现敌人的队友就没有），
    /// 借同一个主人手下、有目标的队友记下的敌人位置，挑离自己最近的。谁都没有返回 false，这只照常跟随
    static bool ChargePoint(BotOwner bot, Player owner, out Vector3 point)
    {
        if (EnemyPoint(bot.Memory?.GoalEnemy, out point)) return true;
        var best = float.MaxValue;
        foreach (var (mate, o) in EscortRegistry.Bots)   // 每帧都来（Active + Update）：直接走名单，不用迭代器，不分配
        {
            if (o != owner || mate == bot || mate == null || mate.IsDead || !EnemyPoint(mate.Memory?.GoalEnemy, out var p)) continue;
            var d = (p - bot.Position).sqrMagnitude;
            if (d < best) { best = d; point = p; }
        }
        return best < float.MaxValue;
    }

    /// 敌人在哪：看得见是真位置，看不见是组里记的最后位置；读不出来（敌人刚死、组里的记录清掉了，或者从没记过）返回 false
    static bool EnemyPoint(EnemyInfo e, out Vector3 p)
    {
        p = default;
        if (e == null) return false;
        try { p = e.IsVisible ? e.CurrPosition : e.EnemyLastPosition; }
        catch { return false; }
        return p.sqrMagnitude > 0.01f;
    }

    public class EscortLayer : VisitLayer
    {
        bool _returning;   // 牵绳回收中
        bool _yielded;     // 正让给 SAIN / 原版 AI 打：距离线放宽 15 米再收回来，免得敌人在 50 米上下晃时两边来回切

        public EscortLayer(BotOwner bot, int priority) : base(bot, priority) { }

        protected override string LayerName => "VisitAPI Escort";

        protected override Type Logic => typeof(EscortFollow);

        protected internal override VisitLogic NewLogic() => new EscortFollow(BotOwner);

        protected override bool Active()
        {
            var owner = EscortRegistry.OwnerOf(BotOwner);
            if (!Raid.Alive(owner)) return false;
            var mem = BotOwner.Memory;
            if (mem == null) return false;
            var underFire = mem.IsUnderFire;
            var mode = EscortRegistry.ModeOf(owner);
            if (mode == EscortMode.Charge)
            {
                // 10-26：只有到期才把整队改回跟随。这只自己没目标就改的话，一改就是全队，SAIN 下喊完冲往往第一下就没了（SORA：「只有一个听从了指挥」）
                if (EscortRegistry.ChargeExpired(owner)) EscortRegistry.ModeTo(owner, mode = EscortMode.Follow);
                else if (mem.GoalEnemy != null) return mem.GoalEnemy.Distance > 12f;   // 冲到跟前就交给兵自己的 AI（SAIN 或原版）
                else if (ChargePoint(BotOwner, owner, out _)) return true;              // 跟着队友冲：自己还没发现敌人，交给它的 AI 也没东西可打
                else mode = EscortMode.Follow;                                          // 谁都没目标：这只照常跟随，整队的冲留着
            }
            _yielded = Fighting(BotOwner, _yielded ? FightRange + 15f : FightRange);
            if (mode == EscortMode.Hold) return !_yielded && !underFire;
            var far = Far(BotOwner, owner);
            if (_returning)
            {
                if (far < LeashDone) _returning = false;
                else return !underFire || far > 2f * Leash;
            }
            if (far > Leash && !underFire) { _returning = true; return true; }
            return !_yielded && !underFire;
        }
    }

    public class EscortFollow : MoveLogic
    {
        // 宽队形（主人前进方向坐标系：x 右、z 前）：左后、右后、左后远、右后远；隔墙 / 窄处退成纵队（踩着主人的脚印往回 2.5 / 4.5 / 6.5 / 8.5 米）
        static readonly Vector3[] Wide = { new(-2.5f, 0f, -3f), new(2.5f, 0f, -3f), new(-1.5f, 0f, -6f), new(1.5f, 0f, -6f) };
        static readonly float[] Column = { 2.5f, 4.5f, 6.5f, 8.5f };
        const float ColumnHold = 3f;   // 宽位被挡过一次，至少保持纵队这么多秒
        const float Arrive = 1.2f;     // 到位就停
        const float Leave = 3f;        // 停下以后，目标离停下时的位置挪出这么远才重新走
        const float SprintOn = 14f, SprintOff = 7f;
        const float Lead = 0.7f, LeadMax = 3f;   // 主人在走：位子按他这么多秒后的位置算（最多往前这么多米）
        const float WalkFull = 3.5f;             // 兵全速走大约每秒这么多米：把主人的步子换算成兵的速度档

        Vector3 _goal, _anchor, _lastPos, _ownerLast, _ownerVel;
        float _lastPathAt = -10f, _movedAt, _farSince, _nextRescue, _nextCare, _nextLook, _ownerSampleAt, _ownerSampledAt, _ownerMovedAt = -10f, _columnUntil;
        float _aidAt, _stimAt, _surgeryAt, _reloadAt, _doorSince;
        bool _parked;
        int _stuckTries;
        /// _stuckTries 的「Rescue 接管」值（10-29）：离主人 25 米外卡住时，Approach 第二段不再停车、不再每帧重算——以前两边每帧互相打架
        /// （Approach 停车 → Rescue 解除 → 下一帧又停车），兵冻在原地直到传送成功，「卡住 8 秒先强制重算」那一档从来没生效过
        const int RescueStuck = 2;

        public EscortFollow(BotOwner bot) : base(bot) { }

        public override void Start()
        {
            base.Start();
            _parked = false; _lastPathAt = -10f; _stuckTries = 0; _farSince = 0f; _doorSince = 0f;
            _lastPos = BotOwner.Position; _movedAt = Time.time;
        }

        public override void Update()
        {
            var owner = EscortRegistry.OwnerOf(BotOwner);
            if (owner == null) return;
            try
            {
                if ((BotOwner.Position - _lastPos).sqrMagnitude > 0.25f) { _lastPos = BotOwner.Position; _movedAt = Time.time; _stuckTries = 0; }
                if (Time.time > _ownerSampleAt)   // 主人在不在走、走多快：每 0.25 秒看一眼，挪了 0.3 米以上算在走
                {
                    var step = Flat(owner.Position - _ownerLast);
                    var dt = Time.time - _ownerSampledAt;
                    _ownerVel = dt > 0.05f && dt < 1f ? step / dt : Vector3.zero;
                    if (step.sqrMagnitude > 0.09f) _ownerMovedAt = Time.time;
                    _ownerLast = owner.Position; _ownerSampledAt = Time.time; _ownerSampleAt = Time.time + 0.25f;
                }
                SelfCare(owner);
                if (InSurgery()) { Park(); BotOwner.SetPose(0.6f); return; }   // 手术包要站定（蹲着做）
                switch (EscortRegistry.ModeOf(owner))
                {
                    case EscortMode.Hold:
                        if (Approach(EscortRegistry.HoldPoint(BotOwner), 1.5f, false)) BotOwner.LookData?.SetLookPointByHearing();
                        break;
                    case EscortMode.Come:
                        if (Approach(owner.Position, 3f, true)) EscortRegistry.ModeTo(owner, EscortMode.Follow);
                        else Rescue(owner);
                        break;
                    case EscortMode.Charge:
                        if (ChargePoint(BotOwner, owner, out var at)) Charge(at);
                        else { Formation(owner); Rescue(owner); }   // 谁都没目标：照常跟随
                        break;
                    default:
                        Formation(owner);
                        Rescue(owner);
                        break;
                }
            }
            catch (Exception e) { Plugin.WarnOnce("escort-follow", "[escort] follow step failed, escorts may stop following: " + e.Message); }
        }

        // ───────────── 走路 ─────────────

        bool Busy => BotOwner.Medecine?.Using == true || BotOwner.WeaponManager?.Reload?.Reloading == true;

        /// 速度以 Mover 上的为准（10-26）：原版靠近门时每帧把它压到 0.5（BotMover.method_19）；
        /// 和想要的不一样就改回来，门口照样减速（原版每帧都会再压一次）
        void SetSpeed(float s)
        {
            if (Mathf.Abs(BotOwner.Mover.DestMoveSpeed - s) >= 0.01f) BotOwner.SetTargetMoveSpeed(s);
        }

        void Park()
        {
            if (!_parked) { _parked = true; _anchor = _goal; }
            BotOwner.StopMove();
            SetSprint(false);
        }

        /// 主人在走时不开「快到终点减速」：目标点就在几米外，开着它就一直在减速区里，走到头停一下再追（10-25 夜实机「一顿一顿」）；主人站定了，最后几步才减速
        void Path(Vector3 target, float arrive, bool force = false)
        {
            _goal = target; _lastPathAt = Time.time;
            var r = Mathf.Max(arrive - 0.4f, 0.5f);
            var slow = !OwnerMoving;
            if (BotOwner.GoToPoint(target, slow, r, false, true, true, false, force) != NavMeshPathStatus.PathComplete)
                BotOwner.GoToPoint(target, slow, r, false, false, true, false, force);   // 整条路不通就走到能走的最近处
        }

        bool OwnerMoving => Time.time - _ownerMovedAt < 0.6f;

        /// 走到 target 的 arrive 米内就停下（返回 true）。停下以后目标离停下时的位置挪出 Leave 米（主人在走时 1.5 米）才重新走，免得走一步停一步；
        /// 主人一直在走就不停车，跟着他的步子一路走（不然就是「走一下停一下」）
        bool Approach(Vector3 target, float arrive, bool allowSprint, bool follow = false)
        {
            var d = Flat(target - BotOwner.Position).magnitude;
            var moving = OwnerMoving;
            if (_parked)
            {
                if (Flat(target - _anchor).magnitude <= (moving ? 1.5f : Leave)) return true;
                _parked = false; _movedAt = Time.time; _lastPos = BotOwner.Position; _stuckTries = 0; _lastPathAt = -10f;
            }
            // 位子被别的护卫占了：到了 3 米内就算到
            if (d <= 3f && EscortRegistry.Crowded(BotOwner, target, 1.2f) || d <= arrive && !moving) { _goal = target; Park(); return true; }
            if (d <= arrive) { _movedAt = Time.time; return true; }   // 主人还在走、自己已经贴在位子上：不停车也不算卡住
            DoorStep();
            if (PassingDoor)
            {
                // 过门：路由 DoorOpener 自己发，不插手；最多当 6 秒「在走」，门卡住了（锁着 / 别的兵堵着）照常走下面卡住那套
                if (_doorSince <= 0f) _doorSince = Time.time;
                if (Time.time - _doorSince < 6f) { _movedAt = Time.time; SetSprint(false); return false; }
            }
            else _doorSince = 0f;
            var since = Time.time - _lastPathAt;
            // 主人在走：半秒一看、目标挪了 0.5 米就重算（目标在前头，路不会走到头）；站着：目标挪了 2 米才重算。
            // 10-26：路没了（走到头了，或者被停掉了：SAIN 交还走路那一下就会停一次）0.3 秒后就重算，不再干等 2 秒。原版开门时换到门边的走法，那会儿不插手
            var lost = !BotOwner.Mover.HasPathAndNoComplete && BotOwner.Mover.CurrentState == EBotMoverState.Default;
            if (since > 2f || lost && since > 0.3f || (moving ? since > 0.5f && (target - _goal).sqrMagnitude > 0.25f : since > 0.4f && (target - _goal).sqrMagnitude > 4f)) Path(target, arrive);
            // 走着却没挪窝（被墙 / 栏杆 / 别的兵挡住原地踏步）：先强制重算一次路，再不动就停下，等目标挪开再走；掉队 25 米外的卡住归 Rescue 管（RescueStuck）
            if (Time.time - _movedAt > 2.5f && _stuckTries != RescueStuck)
            {
                if (_stuckTries == 0) { _stuckTries = 1; _movedAt = Time.time; Path(target, arrive, true); }
                else { _goal = target; Park(); return true; }
            }
            FaceTravel();   // 先转向再要冲刺：SAIN 只让对着路的方向（20° 内）的兵冲
            SetSprint(allowSprint && !Busy && !DoorBlocksRun && WantSprint(d, SprintOn, SprintOff));
            SetSpeed(Sprinting ? 1f
                : follow && moving && !Busy ? FollowSpeed(d)                // 跟着走：和主人的步子对齐
                : !Busy && (moving || d > 6f) ? 1f : 0.7f);
            return false;
        }

        /// 跟随的步速：离位子 4 米外全速追；近了按主人每秒走几米换成兵的速度档，不冲到前头再停下等。0.05 一档，不每帧改
        float FollowSpeed(float d)
        {
            var match = Mathf.Clamp(_ownerVel.magnitude / WalkFull, 0.35f, 1f);
            var s = d > 4f ? 1f : Mathf.Lerp(match, 1f, Mathf.InverseLerp(1.5f, 4f, d));
            return Mathf.Round(s * 20f) / 20f;
        }

        /// 阵型位：从主人脚下沿导航网格拉直线到宽队形的位子，中间隔墙 / 栏杆就换纵队位。
        /// lead：主人在走时先把「脚下」往他前进的方向挪一点（被墙挡就停在挡住处），位子跟着往前。
        /// 10-26（SORA 10-25 夜：「还是有一些，但是我感觉是场景不够开阔的原因」）：纵队踩着主人的脚印排，沿他真正走过的路往回数
        /// （沿前进方向直线往后的话，拐弯时那个点常常在墙里，被挡住的位置跟着乱跳）；宽位被挡过一次，至少保持纵队 3 秒，不在两种位子之间来回切。
        /// 脚印还不够长（刚开局、刚传送、刚掉头）就照旧直线往后排
        Vector3 Slot(Player owner, Vector3 heading, Vector3 lead, List<Vector3> trail)
        {
            var right = Vector3.Cross(Vector3.up, heading);
            var i = Mathf.Clamp(EscortRegistry.SlotOf(BotOwner), 0, Wide.Length - 1);
            var origin = owner.Position;
            if (NavMesh.SamplePosition(origin, out var o, 2f, NavMesh.AllAreas)) origin = o.position;
            if (lead.sqrMagnitude > 0.01f)
            {
                var ahead = origin + lead;
                origin = NavMesh.Raycast(origin, ahead, out var stop, NavMesh.AllAreas) ? stop.position : OnMesh(ahead);
            }
            var wide = origin + right * Wide[i].x + heading * Wide[i].z;
            if (NavMesh.Raycast(origin, wide, out _, NavMesh.AllAreas)) _columnUntil = Time.time + ColumnHold;
            else if (Time.time >= _columnUntil) return OnMesh(wide);
            // 主人在走：往回少数一点（最多 1 米），位子在兵前头一直有路
            if (TrailPoint(trail, owner.Position, Column[i] - Mathf.Min(lead.magnitude, 1f), out var step)) return OnMesh(step);
            var col = origin - heading * Column[i];
            return NavMesh.Raycast(origin, col, out var hit, NavMesh.AllAreas) ? hit.position : OnMesh(col);
        }

        /// 脚印上、从 from（主人现在站的地方）沿走过的路往回数 back 米的点；脚印不够长返回 false
        static bool TrailPoint(List<Vector3> trail, Vector3 from, float back, out Vector3 point)
        {
            point = from;
            for (var k = trail.Count - 1; k >= 0; k--)
            {
                var seg = Vector3.Distance(point, trail[k]);
                if (seg > 0.01f && seg >= back) { point = Vector3.Lerp(point, trail[k], back / seg); return true; }
                back -= seg;
                point = trail[k];
            }
            return false;
        }

        static Vector3 OnMesh(Vector3 p) => NavMesh.SamplePosition(p, out var h, 1.5f, NavMesh.AllAreas) ? h.position : p;

        void Formation(Player owner)
        {
            var heading = EscortRegistry.Heading(owner);
            var trail = EscortRegistry.Trail(owner);   // 宽队形时也每帧记：退成纵队那一刻，身后得已经有脚印
            var far = Far(BotOwner, owner);
            // 主人在走：位子按他一小会儿后的位置算，跟着的人前头一直有路（10-25 夜实机「一顿一顿」）；主人冲刺、自己又落下 8 米以上就一起冲刺
            var lead = OwnerMoving ? Vector3.ClampMagnitude(_ownerVel * Lead, LeadMax) : Vector3.zero;
            var target = far > 40f ? owner.Position : Slot(owner, heading, lead, trail);   // 掉太远先追人，不摆阵
            if (!Approach(target, Arrive, far > 15f || owner.IsSprintEnabled && far > 8f, true) || !_parked) return;
            BotOwner.SetPose(1f);
            // 站定时跟主人看同一个方向：主人瞄的敌人它们也看得见，看见了就会让兵自己的 AI 接手开火（走着的时候朝向交给 Mover，不抢）
            if (Time.time > _nextLook)
            {
                _nextLook = Time.time + 0.5f;
                var look = Flat(owner.LookDirection);
                BotOwner.Steering.LookToDirection(look.sqrMagnitude > 0.01f ? look.normalized : heading);
            }
        }

        void Charge(Vector3 target)
        {
            Approach(target, 10f, true);
            BotOwner.Steering.LookToPoint(target);
        }

        /// 掉队兜底：离主人 25 米外才管。卡住 8 秒先强制重算路；再卡（15 秒）、或者 60 米外追了 10 秒还追不上，而主人没看着这边 → 传送到主人身后。
        /// 这个距离上的卡住只归这里管（RescueStuck）；回到 25 米内交还 Approach——它见到 1 就直接停车，等主人走开再走
        void Rescue(Player owner)
        {
            var far = Far(BotOwner, owner);
            if (far < 25f) { _farSince = 0f; if (_stuckTries == RescueStuck) _stuckTries = 1; return; }
            if (_farSince <= 0f) _farSince = Time.time;
            if (_parked) { _parked = false; _lastPathAt = -10f; _stuckTries = RescueStuck; }   // 这么远了，停着的也得走
            var stuck = Time.time - _movedAt;
            var lost = far > 60f && Time.time - _farSince > 10f;
            if (stuck < 8f && !lost) return;
            if (Time.time < _nextRescue) return;
            _nextRescue = Time.time + 5f;
            if (stuck < 15f && !lost) { Path(owner.Position, 2f, true); return; }
            if (Teleport(owner)) { _farSince = 0f; _movedAt = Time.time; _lastPos = BotOwner.Position; _lastPathAt = -10f; }
        }

        /// 传到主人身后 7～17 米、左右错开的导航网格点上；主人正看着的方向（80° 内）和楼上楼下都不传。
        /// 传完照 SAIN 换层时的做法把原版 Mover 记的路点 / 贴网格点都对齐到新位置，不然它会以为自己还在老地方
        bool Teleport(Player owner)
        {
            var fwd = Flat(owner.LookDirection);
            if (fwd.sqrMagnitude < 0.01f) fwd = Flat(owner.Transform.rotation * Vector3.forward);
            if (fwd.sqrMagnitude < 0.01f) return false;
            fwd.Normalize();
            var right = Vector3.Cross(Vector3.up, fwd);
            var side = EscortRegistry.SlotOf(BotOwner) % 2 == 0 ? -1f : 1f;
            for (var i = 0; i < 6; i++)
            {
                var p = owner.Position - fwd * (7f + 2f * i) + right * side * (2f + 0.7f * i);
                if (!NavMesh.SamplePosition(p, out var hit, 3f, NavMesh.AllAreas)) continue;
                if (Mathf.Abs(hit.position.y - owner.Position.y) > 2.5f) continue;
                if (Vector3.Angle(fwd, Flat(hit.position - owner.Position)) < 80f) continue;
                var at = hit.position;
                BotOwner.StopMove();
                BotOwner.GetPlayer.Teleport(at + Vector3.up * 0.25f, false);
                try
                {
                    var m = BotOwner.Mover;
                    m.PositionOnWayInner = at;
                    m._lastGoodCastPoint = m._prevSuccessLinkedFrom = m._prevLinkPos = at;
                    m.SetPlayerToNavMesh(at);
                }
                catch (Exception e) { Plugin.WarnOnce("escort-relink", "[escort] could not resync the mover after a teleport: " + e.Message); }
                return true;
            }
            return false;
        }

        // ───────────── 顾自己（照 SAIN 4.5.1 SelfActionDecisionClass / SAINSelfActionClass 的调用） ─────────────

        bool InSurgery() => BotOwner.Medecine?.SurgicalKit?.Using == true;

        void SelfCare(Player owner)
        {
            if (Time.time < _nextCare) return;
            _nextCare = Time.time + 0.5f;
            var med = BotOwner.Medecine;
            var wm = BotOwner.WeaponManager;
            var me = BotOwner.GetPlayer;
            if (med == null || wm == null || me == null || BotOwner.IsDead) return;
            if (med.Using || wm.Reload == null || wm.Reload.Reloading || wm.Grenades?.ThrowindNow == true) return;
            if (me.HandsController != null && me.HandsController.IsInInteractionStrictCheck()) return;
            // 1) 重伤 / 濒死先打针（SAIN 也只在这两档用针）
            var status = me.HealthStatus;
            var stims = med.Stimulators;
            if ((status == ETagStatus.BadlyInjured || status == ETagStatus.Dying) && stims != null && stims.HaveSmt
                && Time.time > _stimAt && Time.time - stims.LastEndUseTime > 3f && stims.CanUseNow())
            {
                _stimAt = Time.time + 3f;
                stims.TryApply(false, null, null);
                return;
            }
            // 2) 流血 / 掉血：急救包、绷带、止血带（BotFirstAid 自己挑部位和药）
            var aid = med.FirstAid;
            if (aid != null && Time.time > _aidAt && aid.ShallStartUse())
            {
                _aidAt = Time.time + 5f;
                aid.TryApplyToCurrentPart(null, null);
                return;
            }
            // 3) 断肢：手术包要站定，离主人 25 米内才做（远了先归队）
            var kit = med.SurgicalKit;
            if (kit != null && Time.time > _surgeryAt && Far(BotOwner, owner) < 25f && kit.ShallStartUse())
            {
                _surgeryAt = Time.time + 5f;
                BotOwner.StopMove();
                kit.ApplyToCurrentPart(null);
                return;
            }
            // 4) 弹匣不到七成就换（SAIN 平时的换弹线）
            Reload(wm);
        }

        void Reload(BotWeaponManager wm)
        {
            if (Time.time < _reloadAt || wm.IsMelee || !wm.IsReady) return;
            var r = wm.Reload;
            var sc = wm.ShootController;
            if (r == null || r.Reloading || sc == null || !sc.CanStartReload()) return;
            if (wm.Malfunctions != null && wm.Malfunctions.HaveMalfunction() && wm.Malfunctions.MalfunctionType() != Weapon.EMalfunctionState.Misfire) return;
            var max = r.MaxBulletCount;
            if (max <= 0 || (float)r.BulletCount / max >= 0.7f) return;
            _reloadAt = Time.time + 3f;
            r.TryReload();   // 原版：能换就结束射击、换弹匣或压子弹；没弹了它自己切副武器
        }
    }
}
