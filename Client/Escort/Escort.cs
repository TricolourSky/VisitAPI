using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Communications;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace VisitAPI.Native;

/// <summary>10-16 AI 队友（护卫）。最早是 SORA 10-15 定的 Abyss 福利：「章节进度到一定程度可呼叫 1～4 名黑色军团随玩家进战局保护」；入口是对话里的 `set: escort=N`。
/// 开局（刷兵器就绪后）把战局里每个真人的档案 id 发给服务端问订单，谁有订单就在谁身边刷几只队友，一单一组、Id_spawn 写 visitapi_escort（MoreBots 不给它挂猎杀）。刷完让服务端清零。
/// 10-25 SORA「模组作者肯定会用别的什么BOT」「就纯API」：兵种、难度、就位提示里的称呼都由剧情包的 pack.json 写（服务端 EscortConfig），跟着订单下发；
/// 没有包写就是游戏自带的 Rogue（exUsec），什么模组都不用装。跟随层也由插件自己托管（BotLayers），不再要 BigBrain。
/// 盟友：激活回调里把全部真人登记进组的 Allies（EscortGuards 再把 AddEnemy 对真人堵死）。
/// Fika：只有主机跑（客机跳过），客机玩家在主机上是 ObservedCoopPlayer，当 Player 用；头 5 分钟每 5 秒扫一次晚进来的人。
/// 日志只在出问题时写（订单有但刷不出来 / 兵种不存在）。
/// 10-24：等刷兵器、选锚点、生成、逐只激活走 StorySpawns.WaitSpawner / Activate。</summary>
public static class Escort
{
    /// 订单里没写兵种（10-25 以前的服务端）就照老规矩刷黑色军团
    const string LegacyRole = "blackDivAssault";
    /// 10-18 SORA 同伴：AstralDivide 用预补丁加进游戏的兵种（穿 OTs-14、拿那把改好的 M700），一单只有她一个、名字就叫 SORA
    public const string SoraRole = "soraFox";
    public const int Max = 4;
    const float Radius = 6f;
    static readonly MongoID VarId = Vars.Id("escort");
    static readonly HashSet<string> _seen = new();
    static Dictionary<string, string> _label;   // 最近一张订单带来的称呼（按语言），就位提示和语音指挥的提示用

    public static void Begin(GameWorld world)
    {
        EscortRegistry.Reset();
        _seen.Clear();
        Plugin.Instance.StartCoroutine(Run(world));
    }

    /// 就位 / 指挥提示里怎么称呼队友：包里写了就按玩家语言取（没有这门语言退回中文 / 英文），没写、或写的是空白就是「队友」；两头的空格去掉
    public static string Label
    {
        get
        {
            var s = Loc.Tr(_label, null)?.Trim();
            return string.IsNullOrEmpty(s) ? Loc.Pick("队友", "Teammates") : s;
        }
    }

    static IEnumerator Run(GameWorld world)
    {
        if (StorySpawns.FikaGuest()) yield break;   // 客机先判，不白等刷兵器
        BotSpawner spawner = null;
        yield return StorySpawns.WaitSpawner(s => spawner = s);
        if (spawner == null) yield break;
        var until = Time.unscaledTime + 300f;
        while (Time.unscaledTime < until && world != null)
        {
            // Scav 身份的真人不问：订单在 PMC 档上，Scav 档的 id 服务端对不上任何 PMC（10-29 以前每个 Scav 局都让服务端抛一次异常、客户端白重试三次）
            var humans = (world.AllAlivePlayersList ?? new List<Player>())
                .Where(p => p != null && !p.IsAI && Raid.Alive(p) && !string.IsNullOrEmpty(p.ProfileId) && !_seen.Contains(p.ProfileId) && p.Side != EPlayerSide.Savage).ToList();
            if (humans.Count > 0)
            {
                foreach (var h in humans) _seen.Add(h.ProfileId);
                yield return Serve(world, spawner, humans);
            }
            yield return new WaitForSeconds(5f);
        }
    }

    /// 订单变量服务端自己认（EscortVars.Order），这里只报档案 id
    static string Body(IEnumerable<string> ids) =>
        "{\"profiles\":[" + string.Join(",", ids.Select(i => "\"" + i + "\"")) + "]}";

    /// 一个人的订单：几个队友、各叫什么（服务端按兵种的名字表抽）、什么兵种 / 难度，带不带 SORA
    class Order { public int Count; public List<string> Names = new(); public bool Sora; public string Role; public string Difficulty; public Dictionary<string, string> Label; }

    public static bool IsSora(BotOwner bot) => bot?.Profile?.Info?.Settings?.Role.ToString() == SoraRole;

    static IEnumerator Serve(GameWorld world, BotSpawner spawner, List<Player> humans)
    {
        Dictionary<string, Order> orders = null;
        yield return VisitHttp.Call("/visitapi/escort/orders", Body(humans.Select(h => h.ProfileId)), s =>
        {
            try
            {
                if (!(JObject.Parse(s)["data"] is JObject d)) return false;
                orders = new Dictionary<string, Order>();
                foreach (var p in d.Properties())
                    if (p.Value is JObject o)
                        orders[p.Name] = new Order
                        {
                            Count = o["count"]?.Value<int>() ?? 0,
                            Names = (o["names"] as JArray)?.Select(x => x.Value<string>()).Where(x => !string.IsNullOrEmpty(x)).ToList() ?? new List<string>(),
                            Sora = o["sora"]?.Value<bool>() ?? false,
                            Role = o["role"]?.Value<string>(),
                            Difficulty = o["difficulty"]?.Value<string>(),
                            Label = (o["label"] as JObject)?.Properties().Where(x => x.Value.Type == JTokenType.String)
                                .ToDictionary(x => x.Name, x => x.Value.Value<string>(), StringComparer.OrdinalIgnoreCase)
                        };
                return true;
            }
            catch { return false; }
        }, "[escort]", _ => { });
        if (orders == null || orders.Count == 0) yield break;

        var served = new List<string>();
        foreach (var h in humans)
        {
            if (!orders.TryGetValue(h.ProfileId, out var order) || (order.Count <= 0 && !order.Sora)) continue;
            int got = 0, sora = 0;
            if (order.Label != null) _label = order.Label;
            // SORA 先刷：阵型的第一个位子（离主人最近的那个）是她
            if (order.Sora) yield return SpawnFor(spawner, h, SoraRole, BotDifficulty.normal, 1, new List<string> { "SORA" }, c => sora = c);
            if (order.Count > 0)
                yield return SpawnFor(spawner, h, string.IsNullOrWhiteSpace(order.Role) ? LegacyRole : order.Role.Trim(), Difficulty(order.Difficulty),
                    Math.Min(order.Count, Max), order.Names, c => got = c);
            if (got + sora <= 0) { Plugin.Log.LogWarning($"[escort] could not spawn escorts for {h.Profile?.Nickname}; the order stays for the next raid"); continue; }
            served.Add(h.ProfileId);
            if (!h.IsYourPlayer) continue;
            if (sora > 0) Notice(Loc.Pick("SORA 已就位", "SORA is in position"));
            if (got > 0) Banner(got, order.Names);
        }
        if (served.Count == 0) yield break;
        VisitHttp.Post("/visitapi/escort/consume", Body(served), "[escort]");
        // 本机玩家的本地档案也清零（服务端那份 consume 已经清了；客机的本地值等它下次刷新档案，主机只认服务端的数，不会重复刷）
        var me = humans.FirstOrDefault(h => h.IsYourPlayer && served.Contains(h.ProfileId));
        if (me != null)
            try
            {
                me.Profile?.ProfileVariables?.SetVariableValue(VarId, 0);
                VisitBackend.Variables?.SetVariableValue(VarId, 0);
            }
            catch (Exception e) { Plugin.WarnOnce("escort-clear", "[escort] could not clear the local escort order: " + e.Message); }
    }

    /// 包里写的难度（easy / normal / hard / impossible），认不出就 normal（10-25 以前护卫一律 normal）
    static BotDifficulty Difficulty(string name)
    {
        if (!string.IsNullOrWhiteSpace(name) && Enum.TryParse(name.Trim(), true, out BotDifficulty d) && Enum.IsDefined(typeof(BotDifficulty), d)) return d;
        if (!string.IsNullOrWhiteSpace(name)) Plugin.WarnOnce("escort-difficulty-" + name, $"[escort] unknown escort difficulty '{name}' in a pack's pack.json; using normal");
        return BotDifficulty.normal;
    }

    static IEnumerator SpawnFor(BotSpawner spawner, Player owner, string roleName, BotDifficulty difficulty, int count, List<string> names, Action<int> done)
    {
        if (!Enum.TryParse(roleName, true, out WildSpawnType role))
        {
            var from = roleName == SoraRole ? "AstralDivide and MoreBotsAPI" : "the mod that adds it";
            Plugin.WarnOnce("escort-role-" + roleName, $"[escort] bot role '{roleName}' is unknown to this game install (is {from} installed?); those escorts are skipped");
            done(0); yield break;
        }
        var at = owner.Transform.position;
        // 10-25 夜实机（街区 E2_3，邀请四个只来三个）：出生点窄，6 米内凑不够站位就按凑到的刷了，还不留日志。凑不够先放宽到 10、16 米（同一层楼的规矩不变）
        var points = StorySpawns.PointsAround(at, Radius, count);
        foreach (var wider in WiderRadii)
        {
            if (points.Count >= count) break;
            var more = StorySpawns.PointsAround(at, wider, count);
            if (more.Count > points.Count) points = more;
        }
        if (points.Count == 0) { Plugin.Log.LogWarning($"[escort] no walkable spot within {WiderRadii[WiderRadii.Length - 1]} m of {owner.Profile?.Nickname}; escorts skipped"); done(0); yield break; }
        if (points.Count < count) Plugin.Log.LogWarning($"[escort] only {points.Count}/{count} walkable spots near {owner.Profile?.Nickname}; spawning fewer escorts");
        var arrived = 0;
        var sent = 0;
        yield return StorySpawns.Activate(spawner, role, difficulty, points, at, "visitapi_escort", data =>
        {
            // 10-17 SORA「改成他们的匿名」：用服务端给的代号，和好友名单 / 队伍面板上是同一个名字（Fika 下档案随刷兵包发给客机，名字一起过去）
            var k = 0;
            foreach (var p in data.Profiles)
            {
                if (p?.Info != null && names != null && k < names.Count) p.Info.Nickname = names[k];
                k++;
            }
            foreach (var p in data.Profiles) EscortRegistry.Expect(p?.Id, owner);   // 组在激活回调之前就建好了：补丁靠档案 id 认组
        }, bot => { arrived++; Activated(bot); }, "[escort]", "escorts", n => { sent = n; done(n); });
        if (sent > 0) Plugin.Instance.StartCoroutine(CheckArrived(owner, roleName, sent, () => arrived));
    }

    static readonly float[] WiderRadii = { 10f, 16f };

    /// 发出去的激活是异步的（原生 BotCreator 装模型、建大脑），有的会悄悄没了下文：半分钟还没到齐就记一笔，下回好查少的那个卡在哪
    static IEnumerator CheckArrived(Player owner, string role, int sent, Func<int> arrived)
    {
        var until = Time.unscaledTime + 30f;
        while (arrived() < sent && Time.unscaledTime < until) yield return null;
        if (arrived() < sent) Plugin.Log.LogWarning($"[escort] {sent} {role} escorts were sent for {owner?.Profile?.Nickname} but only {arrived()} arrived after 30 s");
    }

    /// 激活回调：对上主人；全部真人登记成盟友（敌人表里已经有的先退掉）。盟友表 SAIN 每次刷新都会读，原版 / MoreBots 走 AddEnemy 的路被 EscortGuards 堵死。
    /// 再把 SAIN 性格改成敢推的（EscortSain，SORA 19:08 实机嫌它们只会找掩体）
    static void Activated(BotOwner bot)
    {
        var owner = EscortRegistry.Activated(bot);
        if (owner == null) return;
        try
        {
            var g = bot.BotsGroup;
            foreach (var h in Singleton<GameWorld>.Instance?.AllAlivePlayersList ?? new List<Player>())
            {
                if (h == null || h.IsAI) continue;
                g.RemoveEnemy(h);
                g.AddAlly(h);
            }
        }
        catch (Exception e) { Plugin.WarnOnce("escort-ally", "[escort] could not mark players as allies, escorts may turn hostile: " + e.Message); }
        BotLayers.Ensure(bot);   // 大脑已经激活了就当场插跟随层；还没激活的由大脑激活后缀插（BotLayers.Attach）
        Plugin.Instance.StartCoroutine(EscortSain.Aggressive(bot));
    }

    static void Banner(int n, List<string> names)
    {
        var who = names != null && names.Count > 0 ? string.Join(Loc.Pick("、", ", "), names.Take(n)) : "×" + n;
        Notice(Loc.Pick($"{Label}已就位：{who}", $"{Label} in position: {who}"));
    }

    static void Notice(string text)
    {
        try { NotificationManager.DisplayMessageNotification(text); }
        catch (Exception e) { Plugin.WarnOnce("escort-banner", "[escort] could not show the escort notice: " + e.Message); }
    }
}
