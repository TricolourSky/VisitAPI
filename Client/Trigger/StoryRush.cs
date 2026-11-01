using System.Collections.Generic;
using EFT;

namespace VisitAPI.Native;

/// <summary>10-17「看门狗」：剧情刷出来「冲着玩家去」的兵（spawns 里 attack: true）→ 它们要追的玩家。一局一清（RaidStart）。
/// 真正让兵往前压的是 EscortBrain.RushLayer，10-25 起由插件自己托管（BotLayers 看这份名单给兵插层），不再要 BigBrain。</summary>
public static class StoryRush
{
    static readonly Dictionary<BotOwner, Player> _targets = new();

    public static void Reset() => _targets.Clear();

    public static void Add(BotOwner bot, Player target)
    {
        if (bot != null && target != null) _targets[bot] = target;
    }

    public static bool Has(BotOwner bot) => bot != null && _targets.ContainsKey(bot);

    public static Player TargetOf(BotOwner bot) => bot != null && _targets.TryGetValue(bot, out var t) ? t : null;
}
