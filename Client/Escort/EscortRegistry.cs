using System.Collections.Generic;
using EFT;
using UnityEngine;
using UnityEngine.AI;

namespace VisitAPI.Native;

public enum EscortMode { Follow, Hold, Come, Charge }

/// <summary>10-16 黑色军团护卫：谁是护卫、跟谁走、主人下了什么令。生成前按档案 id 登记（BotsGroup 在激活回调之前就建好了，补丁只能靠 id 认组），
/// 激活后换成 BotOwner → 主人（Player；Fika 客机玩家在主机上是 ObservedCoopPlayer，也是 Player）。一局一清。
/// 10-24：护卫名单是按激活先后排的 List（阵型位次就是这个顺序）；Dictionary 的枚举顺序不是契约</summary>
public static class EscortRegistry
{
    static readonly Dictionary<string, Player> _pendingByProfile = new();   // 护卫档案 id → 主人
    static readonly List<(BotOwner bot, Player owner)> _bots = new();       // 激活先后 = 阵型位次
    static readonly Dictionary<string, EscortMode> _modes = new();           // 主人档案 id → 当前指令
    static readonly Dictionary<BotOwner, Vector3> _holdAt = new();           // 「待命」时站的点
    static readonly Dictionary<string, float> _chargeUntil = new();          // 「冲」的时限
    static readonly Dictionary<string, (Vector3 anchor, Vector3 heading)> _heading = new();   // 主人档案 id → 阵型朝向
    static readonly Dictionary<string, (Vector3 dir, float since)> _facing = new();         // 主人站着时朝哪看、从什么时候起一直这么看
    static readonly Dictionary<string, List<Vector3>> _trail = new();                      // 主人档案 id → 脚印（导航网格上，新的在后）

    public static void Reset() { _pendingByProfile.Clear(); _bots.Clear(); _modes.Clear(); _holdAt.Clear(); _chargeUntil.Clear(); _heading.Clear(); _facing.Clear(); _trail.Clear(); }

    /// 10-26：主人的脚印，纵队踩着它排（EscortFollow.Slot）。走出 0.75 米记一个，贴到导航网格上（贴不上就不记）；只留最近 30 个（20 多米）。
    /// 一步出去 5 米以上（传送）或者掉头往回走（和上一步差 135° 以上）就从头记：掉头以后还往回数，会数到他前头去
    public static List<Vector3> Trail(Player owner)
    {
        var id = owner.ProfileId;
        if (!_trail.TryGetValue(id, out var t)) _trail[id] = t = new List<Vector3>();
        var n = t.Count;
        if (n > 0 && (owner.Position - t[n - 1]).sqrMagnitude < 0.5625f) return t;
        if (!NavMesh.SamplePosition(owner.Position, out var hit, 1.5f, NavMesh.AllAreas)) return t;
        var p = hit.position;
        if (n > 0)
        {
            var step = p - t[n - 1];
            if (step.sqrMagnitude < 0.5625f) return t;
            var last = n > 1 ? t[n - 1] - t[n - 2] : Vector3.zero;
            step.y = 0f; last.y = 0f;
            if ((p - t[n - 1]).sqrMagnitude > 25f || last.sqrMagnitude > 0.01f && Vector3.Angle(step, last) > 135f) t.Clear();
        }
        t.Add(p);
        if (t.Count > 30) t.RemoveAt(0);
        return t;
    }

    /// 10-17：阵型朝向 = 主人的前进方向（走出 1.5 米才更新），不是视线方向。原地转视角时阵型不再跟着绕圈（SORA 实机「原地踏步」的来源之一）。
    /// 10-25 夜 SORA「我停下的时候……要么站着我前面两侧或者左右两边」：站着时朝一个方向看稳 2 秒（偏差 30° 内）、和阵型朝向差出 60° 以上，阵型转到他身后；随便扫一眼不动
    public static Vector3 Heading(Player owner)
    {
        var id = owner.ProfileId;
        var pos = new Vector3(owner.Position.x, 0f, owner.Position.z);
        var look = owner.LookDirection; look.y = 0f;
        look = look.sqrMagnitude > 0.01f ? look.normalized : Vector3.forward;
        if (!_heading.TryGetValue(id, out var h)) h = (pos, look);
        var d = pos - h.anchor;
        if (d.sqrMagnitude > 2.25f) { h = (pos, d.normalized); _facing.Remove(id); }
        else
        {
            if (!_facing.TryGetValue(id, out var f) || Vector3.Angle(f.dir, look) > 30f) f = (look, Time.time);
            _facing[id] = f;
            if (Time.time - f.since > 2f && Vector3.Angle(h.heading, f.dir) > 60f) h = (h.anchor, f.dir);
        }
        _heading[id] = h;
        return h.heading;
    }

    /// 同一个主人手下别的护卫里有没有人站在 p 附近（抢同一个位子就别再往里挤）
    public static bool Crowded(BotOwner self, Vector3 p, float radius)
    {
        var owner = OwnerOf(self);
        foreach (var (bot, o) in _bots)
        {
            if (bot == self || o != owner || bot == null || bot.IsDead) continue;
            var d = bot.Position - p; d.y = 0f;
            if (d.sqrMagnitude < radius * radius) return true;
        }
        return false;
    }

    /// 名单本身（激活先后）：「冲」每帧要扫一遍同一个主人手下的队友（EscortBrain.ChargePoint），直接 foreach 它（List 的枚举器是结构体，不分配）；只读
    internal static List<(BotOwner bot, Player owner)> Bots => _bots;

    public static void Expect(string botProfileId, Player owner) { if (!string.IsNullOrEmpty(botProfileId)) _pendingByProfile[botProfileId] = owner; }

    public static bool IsEscortProfile(string profileId) => profileId != null && _pendingByProfile.ContainsKey(profileId);

    /// 激活回调：把 BotOwner 和主人对上；对不上（不是我们刷的）返回 null
    public static Player Activated(BotOwner bot)
    {
        var id = bot?.Profile?.Id;
        if (id == null || !_pendingByProfile.TryGetValue(id, out var owner)) return null;
        var i = IndexOf(bot);
        if (i < 0) _bots.Add((bot, owner)); else _bots[i] = (bot, owner);
        return owner;
    }

    static int IndexOf(BotOwner bot)
    {
        for (var i = 0; i < _bots.Count; i++) if (_bots[i].bot == bot) return i;
        return -1;
    }

    public static Player OwnerOf(BotOwner bot) { var i = bot != null ? IndexOf(bot) : -1; return i < 0 ? null : _bots[i].owner; }

    public static bool IsEscortBot(BotOwner bot) => bot != null && IndexOf(bot) >= 0;

    /// 组的第一只是护卫 = 整组是护卫组（ShallBeGroup 把一单刷成一组）。构造期间就能判：_initialBot 在循环敌人之前已经赋值
    public static bool IsEscortGroup(BotsGroup group) => group?._initialBot != null && IsEscortProfile(group._initialBot.Profile?.Id);

    public static int Count => _bots.Count;

    /// 主人手下还活着的护卫（语音指挥的提示要按里面有没有 SORA 写，10-18）
    public static IEnumerable<BotOwner> AliveOf(Player owner)
    {
        foreach (var (bot, o) in _bots)
            if (o == owner && bot != null && !bot.IsDead) yield return bot;
    }

    /// 同一个主人手下的第几只（按刷出来的先后），阵型按它分位子
    public static int SlotOf(BotOwner bot)
    {
        var owner = OwnerOf(bot);
        var i = 0;
        foreach (var (b, o) in _bots) { if (o != owner) continue; if (b == bot) return i; i++; }
        return 0;
    }

    public static EscortMode ModeOf(Player owner) => owner != null && _modes.TryGetValue(owner.ProfileId, out var m) ? m : EscortMode.Follow;

    public static void ModeTo(Player owner, EscortMode mode) { if (owner != null) _modes[owner.ProfileId] = mode; }

    /// 主人下令：返回手下还活着的护卫数（0 = 没人听，别弹提示）
    public static int SetMode(Player owner, EscortMode mode)
    {
        if (owner == null) return 0;
        var n = 0;
        foreach (var (bot, o) in _bots)
        {
            if (o != owner || bot == null || bot.IsDead) continue;
            n++;
            if (mode == EscortMode.Hold) _holdAt[bot] = bot.Position;
        }
        if (n == 0) return 0;
        _modes[owner.ProfileId] = mode;
        if (mode == EscortMode.Charge) _chargeUntil[owner.ProfileId] = Time.time + 20f;
        return n;
    }

    public static Vector3 HoldPoint(BotOwner bot) => _holdAt.TryGetValue(bot, out var p) ? p : bot.Position;

    public static bool ChargeExpired(Player owner) => owner == null || !_chargeUntil.TryGetValue(owner.ProfileId, out var t) || Time.time > t;
}
