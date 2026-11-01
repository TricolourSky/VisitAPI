using System;
using System.Collections.Generic;
using EFT;
using HarmonyLib;

namespace VisitAPI.Native;

/// <summary>10-25 SORA：「万一别的作者完全不用任何依赖模组呢。就纯API」——护卫跟随、剧情突入的行为层由插件自己托管，不再借 BigBrain。
/// 原理和 BigBrain 一样（10-25 反编译它核过）：层是游戏原生的 BaseLogicLayer，插进这只兵大脑的层表（AICoreStrategy.TryAddLayer，按优先级排进活动层）；
/// 层给出的动作用我们自己的编号（8800 起：原生的都在 200 以内，BigBrain 认 9000 及以上），原生按编号现建动作节点（AIActionsList.CreateNode）时认出来交给我们。
/// 和 BigBrain 不同的地方：
///   · 只插给护卫和突入兵自己（大脑激活那一刻、或者激活回调里，看名单），别的兵一概不碰；
///   · 不接管原生的选层和节点更新：动作在节点第一次被更新时开始，停下靠层切换事件（OnLayerChangedTo，原生的选层和 BigBrain 换掉的选层都会发）。
/// 装没装 BigBrain / SAIN 都走这一套：BigBrain 换掉了原生的选层和节点更新，但照样按优先级挑活动层、照样发层切换事件、照样经 CreateNode 建节点（9000 以下它放行）。</summary>
public static class BotLayers
{
    const int FirstDecision = 8800;
    const int FirstLayerId = 8800;

    static readonly Dictionary<Type, BotLogicDecision> _decisions = new();
    static readonly Dictionary<BotOwner, VisitLayer> _layers = new();               // 这只兵身上我们的层（每只至多一层）

    /// 动作类型 → 我们的动作编号（第一次用到时分配，进程内不变）
    internal static BotLogicDecision DecisionOf(Type logic)
    {
        if (!_decisions.TryGetValue(logic, out var d))
        {
            d = (BotLogicDecision)(FirstDecision + _decisions.Count);
            _decisions[logic] = d;
        }
        return d;
    }

    static bool Ours(BotLogicDecision d) => (int)d >= FirstDecision && (int)d < FirstDecision + _decisions.Count;

    /// 开局清名单（兵的层随兵的大脑一起释放，这里只是不留旧的引用）
    public static void Reset() => _layers.Clear();

    internal static void Forget(BotOwner bot, VisitLayer layer)
    {
        if (bot != null && _layers.TryGetValue(bot, out var l) && ReferenceEquals(l, layer)) _layers.Remove(bot);
    }

    internal static VisitLayer LayerOf(BotOwner bot) => bot != null && _layers.TryGetValue(bot, out var l) ? l : null;

    /// <summary>这只兵该有我们的层就插上：护卫插跟随层、突入兵插突入层。已经插过、或者大脑还没激活，就什么都不做。
    /// 大脑激活后缀（Attach）和两个激活回调（Escort.Activated / StorySpawns.Attack）都调它，谁先到谁插——
    /// 原生是先调刷兵回调、后在兵自己的更新里激活大脑（BotOwner.method_10），但不靠这个先后。</summary>
    public static void Ensure(BotOwner bot)
    {
        if (bot == null || _layers.ContainsKey(bot)) return;
        var strategy = bot.Brain?.BaseBrain;
        if (strategy?._coreAgent == null) return;
        VisitLayer layer =
            EscortRegistry.IsEscortProfile(bot.Profile?.Id) || EscortRegistry.IsEscortBot(bot) ? new EscortBrain.EscortLayer(bot, EscortBrain.Priority)
            : StoryRush.Has(bot) ? new EscortBrain.RushLayer(bot, EscortBrain.RushPriority)
            : null;
        if (layer == null) return;
        var id = FirstLayerId;
        while (strategy._layers.ContainsKey(id)) id++;
        if (!strategy.TryAddLayer(id, layer, activeOnStart: true)) return;
        // 原生 Activate 对开局就有的层也做这两步（只是大脑调试面板的名单）
        strategy._coreAgent.AddLayer(id, layer.Name(), layer.Priority);
        strategy._coreAgent.ActivateLayer(layer.Name());
        strategy.OnLayerChangedTo += now => { if (!ReferenceEquals(now, layer)) layer.StopLogic(); };
        _layers[bot] = layer;
    }

    /// 大脑激活那一刻：看名单插层
    [HarmonyPatch(typeof(StandartBotBrain), nameof(StandartBotBrain.Activate))]
    public static class Attach
    {
        static void Postfix(StandartBotBrain __instance)
        {
            try { Ensure(__instance?._owner); }
            catch (Exception e) { Plugin.WarnOnce("layers-attach", "[escort] could not attach the follow / rush behaviour; those bots use their own AI: " + e.Message); }
        }
    }

    /// 原生按动作编号现建节点：我们的编号给我们的节点，别的放行。
    /// 动作由层自己 new（NewLogic）。10-25 夜实机：Activator.CreateInstance(logic, bot) 被编译成 (Type, bool) 那个重载——
    /// BotOwner 是 Unity 对象，能隐式转成 bool——去找无参构造就失败了，跟随一次都没跑，护卫蹲着倒退跟人。
    /// 真建不出来就把层标坏（ShallUseNow 给 false），兵整个交还自己的 AI，不留一个空节点占着
    [HarmonyPatch(typeof(AIActionsList), nameof(AIActionsList.CreateNode))]
    public static class CreateNode
    {
        static bool Prefix(BotLogicDecision type, BotOwner bot, ref AICoreNode __result)
        {
            if (!Ours(type)) return true;
            __result = null;
            var layer = LayerOf(bot);
            try
            {
                if (layer != null) __result = new VisitNode(layer.NewLogic(), layer, bot);
            }
            catch (Exception e)
            {
                if (layer != null) layer.Broken = true;
                Plugin.WarnOnce("layers-node", "[escort] could not create the follow / rush action; those bots use their own AI: " + e.GetBaseException().Message);
            }
            return false;
        }
    }
}

/// <summary>我们的行为层：游戏原生的层，只出一种动作（Logic）。Active() 为真时接管这只兵，为假时让给兵自己的层（原版 AI 或 SAIN）</summary>
public abstract class VisitLayer : BaseLogicLayer
{
    internal VisitNode Node;
    internal bool Broken;   // 动作建不出来（CreateNode）：这一局这只兵不再用这层

    protected VisitLayer(BotOwner bot, int priority) : base(bot, priority) { }

    public BotOwner BotOwner => _owner;
    protected abstract string LayerName { get; }
    protected abstract Type Logic { get; }
    protected abstract bool Active();
    /// 这层的动作，原生现建节点时（CreateNode）调
    protected internal abstract VisitLogic NewLogic();

    public override string Name() => LayerName;

    public override bool ShallUseNow()
    {
        if (Broken) return false;
        try { return Active(); }
        catch (Exception e) { Plugin.WarnOnce("layer-" + LayerName, $"[escort] {LayerName} check failed; the bot falls back to its own AI: " + e.Message); return false; }
    }

    public override AICoreActionResult<BotLogicDecision, CoreActionResultParams> GetDecision() => new(BotLayers.DecisionOf(Logic), LayerName);

    /// 别的层留下的动作一律换成我们的；我们自己的动作一直做下去（让不让位由 Active 决定）
    public override AICoreActionEnd ShallEndCurrentDecision(AICoreActionResult<BotLogicDecision, CoreActionResultParams> curDecision) =>
        curDecision.Action == BotLayers.DecisionOf(Logic) ? ContinueNodeLogic : FinishNodeLogic;

    internal void StopLogic() => Node?.Stop();

    public override void Dispose()
    {
        StopLogic();
        BotLayers.Forget(_owner, this);
        base.Dispose();
    }
}

/// <summary>层接管时跑的动作：开始 → 每次被更新 → 停下</summary>
public abstract class VisitLogic
{
    public BotOwner BotOwner { get; }
    /// 装着这个动作的原生节点（VisitNode 建好就填）：走路的动作靠它每帧喂门（MoveNode.DoorOpen），不然兵不会自己开门（10-29）
    internal VisitNode Node;

    protected VisitLogic(BotOwner bot) { BotOwner = bot; }

    public virtual void Start() { }
    public virtual void Stop() { }
    public abstract void Update();
}

/// <summary>原生动作节点的外壳：第一次被更新时开始，层让位（层切换事件）或兵的大脑释放时停下</summary>
sealed class VisitNode : MoveNode<CoreActionResultParams>
{
    readonly VisitLogic _logic;
    bool _running;

    public VisitNode(VisitLogic logic, VisitLayer layer, BotOwner bot) : base(bot)
    {
        _logic = logic;
        logic.Node = this;
        layer.Node = this;
    }

    public override void UpdateNodeByBrain(CoreActionResultParams data)
    {
        try
        {
            if (!_running) { _running = true; _logic.Start(); }
            _logic.Update();
        }
        catch (Exception e) { Plugin.WarnOnce("node-" + _logic.GetType().Name, $"[escort] {_logic.GetType().Name} step failed: " + e.Message); }
    }

    internal void Stop()
    {
        if (!_running) return;
        _running = false;
        try { _logic.Stop(); }
        catch (Exception e) { Plugin.WarnOnce("node-stop-" + _logic.GetType().Name, $"[escort] {_logic.GetType().Name} stop failed: " + e.Message); }
    }

    public override void Dispose()
    {
        Stop();
        base.Dispose();
    }
}
