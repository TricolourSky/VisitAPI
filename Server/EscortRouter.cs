using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Dialog;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;

namespace VisitAPI.Server;

public class EscortRequest : IRequestData
{
    [JsonPropertyName("profiles")] public List<string> Profiles { get; set; }
    /// 10-17：面板上点名邀请的是哪几个（aid 字符串）。只带人数 count 的是 10-16 测试版的老格式，10-17 起不认（审查 E5）
    [JsonPropertyName("members")] public List<string> Members { get; set; }
}

/// <summary>10-16 AI 队友（护卫）的服务端那半（最早是 SORA 10-15 定的 Abyss 福利「章节进度到一定程度可呼叫 1～4 名黑色军团随玩家进战局」）。
/// 订单 = 档案变量 `escort`（对话 `set: escort=N`，或主菜单左下角把队友邀请进队）；上限 = 档案变量 `escort_max`（对话 `set: escort_max=N` 放开，0 = 还没解锁，好友名单里也没有队友）；
/// 点名 = 档案变量 `escort_pick`（第 i 位 = 队友 i，面板邀请时写；对话下的单没有点名，按前 N 个）。
/// 变量 id 和客户端 Vars.Id 同一套散列（MD5("visitapi.var|名字") 前 24 位，DialogTemplateBuilder.Id）。
/// 路由：orders / consume 给主机开局批量读、清各人的订单（Fika 客机的订单主机自己看不到）；party / order 给左下角队伍面板。
/// 10-17 SORA「改成他们的匿名」：队友用兵种自己的代号（名字表），每个玩家按档案 id 固定抽四个，好友名单、队伍面板、进图刷出来的兵是同一个名字。
/// 10-25：兵种、名字表、要剔掉的名字、难度、称呼都由剧情包的 pack.json 写（EscortConfig）；没有包写就是 Rogue（exUsec）。名字表读不到就用内置的几个。</summary>
public static class EscortVars
{
    public const int Max = 4;
    /// 干员假账号的 aid = AidBase + 编号（1～4）
    public const int AidBase = 9900000;
    public static readonly MongoId Order = Id("escort");
    public static readonly MongoId Limit = Id("escort_max");
    public static readonly MongoId Pick = Id("escort_pick");

    /// 10-18 SORA 同伴（Tech Leader：「SORA 跟玩家住在一起了，之后就可以邀请 SORA 一起做任务」）：
    /// 解锁 = 档案变量 sora_party（剧本里找药收尾那句 `set: sora_party=1`），订单 = escort_sora（1 = 下一局带她）。
    /// 她在好友名单里是固定的一个假账号，和干员共用左下角队伍面板，四个位子里占一个。
    /// 兵种 soraFox 是 AstralDivide 加的；没装它或 MoreBots 时客户端认不出兵种，刷不出来、警告一次。
    public static readonly MongoId SoraUnlock = Id("sora_party");
    public static readonly MongoId SoraOrder = Id("escort_sora");
    public const int SoraAid = AidBase + 10;

    static readonly string[] Fallback = { "Cipher", "Dusk", "Banshee", "Atlas", "Cobalt", "Ember", "Fang", "Domino" };
    static List<string> _pool;

    public static MongoId Id(string name) => new(Ids.Hex24("visitapi.var|" + name));

    public static int Get(PmcData pmc, MongoId key) => pmc?.Variables != null && pmc.Variables.TryGetValue(key, out var n) ? n : 0;

    /// 这个档案能带几个：档案变量 escort_max，夹在 0～4
    public static int MaxFor(PmcData pmc) => Math.Clamp(Get(pmc, Limit), 0, Max);

    /// <summary>代号池（排好序，抽签才稳定）：包里写死的名字，或者兵种名字表（EscortConfig.NameTables）里的名字，剔掉 excludeNames。
    /// 够四个就缓存；不够拿内置的补——名字表还没读到（兵种是模组加的、还没注册）时不缓存，下次再读。</summary>
    static List<string> Pool(BotTable bots)
    {
        if (_pool != null) return _pool;
        var cfg = EscortConfig.Current;
        var names = new List<string>(cfg.Names);
        var fromTables = names.Count == 0;
        if (fromTables)
            try
            {
                var tables = cfg.NameTables();
                if (bots?.Types != null)
                    foreach (var kv in bots.Types)
                        if (tables.Contains(kv.Key, StringComparer.OrdinalIgnoreCase) && kv.Value?.FirstNames != null)
                            names.AddRange(kv.Value.FirstNames);
            }
            catch { }
        var clean = names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Where(n => !cfg.ExcludeNames.Contains(n))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.Ordinal).ToList();
        if (clean.Count >= Max) return _pool = clean;
        var filled = clean.Concat(Fallback.Where(f => !clean.Contains(f, StringComparer.OrdinalIgnoreCase))).ToList();
        if (!fromTables) _pool = filled;   // 写死的名字不会再变
        return filled;
    }

    /// 这个玩家的四个干员代号：按档案 id 定种子洗牌取前四，同一个档案永远是这四个
    public static List<string> Callsigns(BotTable bots, string profileId)
    {
        var pool = Pool(bots).ToList();
        var seed = BitConverter.ToInt32(MD5.HashData(Encoding.UTF8.GetBytes("visitapi.escort|" + profileId)), 0);
        var rng = new Random(seed);
        for (var i = 0; i < Max && i < pool.Count; i++) { var j = rng.Next(i, pool.Count); (pool[i], pool[j]) = (pool[j], pool[i]); }
        return pool.Take(Max).ToList();
    }

    /// 被点名的干员编号（1～4）；没点名或人数对不上（对话 set: escort=N 下的单）就按前 N 个
    public static List<int> Picked(PmcData pmc, int count)
    {
        var mask = Get(pmc, Pick);
        var list = Enumerable.Range(1, Max).Where(i => (mask & (1 << (i - 1))) != 0).ToList();
        return list.Count == count ? list : Enumerable.Range(1, Math.Clamp(count, 0, Max)).ToList();
    }

    /// 干员名单：固定四个假账号（aid 9900001～9900004，id 按编号散列），上限几个就给几个
    public static IEnumerable<UserDialogInfo> Operatives(BotTable bots, string profileId, int max)
    {
        var names = Callsigns(bots, profileId);
        return Enumerable.Range(1, Math.Clamp(max, 0, Max)).Select(i => Friend(Id("escort.operative." + i), AidBase + i, names[i - 1]));
    }

    static UserDialogInfo Friend(MongoId id, int aid, string nick) => new()
    {
        Id = id,
        Aid = aid,
        Info = new UserDialogDetails { Nickname = nick, Side = "Savage", Level = 30, MemberCategory = MemberCategory.Default, SelectedMemberCategory = MemberCategory.Default }
    };

    public static bool SoraUnlocked(PmcData pmc) => Get(pmc, SoraUnlock) > 0;

    public static bool SoraOrdered(PmcData pmc) => SoraUnlocked(pmc) && Get(pmc, SoraOrder) > 0;

    /// 面板上的位子：干员上限 + SORA（解锁了就多一个），一共不超过四个
    public static int PanelMax(PmcData pmc) => Math.Min(Max, MaxFor(pmc) + (SoraUnlocked(pmc) ? 1 : 0));

    /// 好友名单 / 队伍面板上的全部同伴：SORA（解锁了才有，排第一）+ 干员（上限几个给几个）
    public static IEnumerable<UserDialogInfo> Companions(BotTable bots, string profileId, PmcData pmc)
    {
        if (SoraUnlocked(pmc)) yield return Friend(Id("escort.sora"), SoraAid, "SORA");
        foreach (var op in Operatives(bots, profileId, MaxFor(pmc))) yield return op;
    }

    /// 点名掩码只留编号最小的 keep 个（SORA 占了一个位子时，干员最多三个）
    public static int Trim(int mask, int keep)
    {
        var kept = 0;
        for (var b = 0; b < Max; b++)
        {
            if ((mask & (1 << b)) == 0) continue;
            if (kept < keep) kept++;
            else mask &= ~(1 << b);
        }
        return mask;
    }

    public static int Bits(int mask) => Enumerable.Range(0, Max).Count(b => (mask & (1 << b)) != 0);
}

[Injectable]
public class EscortRouter(JsonUtil jsonUtil, ProfileHelper profileHelper, SaveServer saves, HttpResponseUtil httpResponse, BotTable bots)
    : StaticRouter(jsonUtil, [
        // 开局：各人几个队友、叫什么（被点名的那几个的代号），带不带 SORA（10-18）；10-25 起带上兵种 / 难度 / 称呼（剧情包 pack.json 的 escort，EscortConfig）
        new RouteAction("/visitapi/escort/orders",
            async (url, info, sessionId, output, ct) =>
            {
                var cfg = EscortConfig.Current;
                var result = new Dictionary<string, object>();
                foreach (var (id, pmc) in ProfilesOf((EscortRequest)info, saves))
                {
                    var sora = EscortVars.SoraOrdered(pmc);
                    var n = Math.Clamp(EscortVars.Get(pmc, EscortVars.Order), 0, EscortVars.Max - (sora ? 1 : 0));
                    if (n <= 0 && !sora) continue;
                    var names = EscortVars.Callsigns(bots, id);
                    result[id] = new
                    {
                        count = n, names = EscortVars.Picked(pmc, n).Select(i => names[i - 1]).ToList(), sora,
                        role = cfg.Role, difficulty = cfg.Difficulty, label = cfg.Label
                    };
                }
                return httpResponse.GetBody(result);
            },
            typeof(EscortRequest)),
        new RouteAction("/visitapi/escort/consume",
            async (url, info, sessionId, output, ct) =>
            {
                foreach (var (id, pmc) in ProfilesOf((EscortRequest)info, saves))
                {
                    if (pmc?.Variables == null) continue;
                    // 和 /visitapi/variable/set 用同一把按档案的锁（ProfileVariableLock 按 sessionId 键，PMC id 就是 sessionId）
                    lock (ProfileVariableLock.For(new MongoId(id))) { pmc.Variables[EscortVars.Order] = 0; pmc.Variables[EscortVars.Pick] = 0; pmc.Variables[EscortVars.SoraOrder] = 0; }
                }
                return httpResponse.EmptyResponse();
            },
            typeof(EscortRequest)),
        // 左下角队伍面板：位子数、当前订单（含 SORA）、点名的是哪几个（SORA 排第一）、同伴名单（SORA + 干员）
        new RouteAction("/visitapi/escort/party",
            async (url, info, sessionId, output, ct) =>
            {
                var pmc = profileHelper.GetPmcProfile(sessionId);
                var opMax = EscortVars.MaxFor(pmc);
                var sora = EscortVars.SoraOrdered(pmc);
                var count = Math.Clamp(EscortVars.Get(pmc, EscortVars.Order), 0, Math.Min(opMax, EscortVars.Max - (sora ? 1 : 0)));
                var picked = EscortVars.Picked(pmc, count).Where(i => i <= opMax).Select(i => (EscortVars.AidBase + i).ToString()).ToList();
                if (sora) picked.Insert(0, EscortVars.SoraAid.ToString());
                return httpResponse.GetBody(new
                {
                    max = EscortVars.PanelMax(pmc), count = count + (sora ? 1 : 0), picked,
                    members = EscortVars.Companions(bots, sessionId.ToString(), pmc).Select(o => new { aid = o.Aid.ToString(), id = o.Id.ToString(), nick = o.Info.Nickname }).ToList()
                });
            },
            typeof(EscortRequest)),
        // 面板上邀请 / 移出一个同伴 → 订单 = 面板上的干员人数 + 带不带 SORA、点名 = 面板上是哪几个干员（按上限截，SORA 占一个位子）
        new RouteAction("/visitapi/escort/order",
            async (url, info, sessionId, output, ct) =>
            {
                var request = (EscortRequest)info;
                var pmc = profileHelper.GetPmcProfile(sessionId);
                if (pmc != null && request?.Members != null)
                {
                    var max = EscortVars.MaxFor(pmc);
                    var sora = EscortVars.SoraUnlocked(pmc) && request.Members.Contains(EscortVars.SoraAid.ToString());
                    var mask = 0;
                    foreach (var aid in request.Members)
                        if (int.TryParse(aid, out var a) && a > EscortVars.AidBase && a <= EscortVars.AidBase + max) mask |= 1 << (a - EscortVars.AidBase - 1);
                    mask = EscortVars.Trim(mask, EscortVars.Max - (sora ? 1 : 0));
                    lock (ProfileVariableLock.For(sessionId))
                    {
                        pmc.Variables ??= new Dictionary<MongoId, int>();
                        pmc.Variables[EscortVars.Order] = EscortVars.Bits(mask);
                        pmc.Variables[EscortVars.Pick] = mask;
                        pmc.Variables[EscortVars.SoraOrder] = sora ? 1 : 0;
                    }
                }
                return httpResponse.EmptyResponse();
            },
            typeof(EscortRequest))
    ])
{
    /// 请求里点名的各档案：只认 24 位十六进制 id；对不上任何 PMC 的跳过。
    /// 10-29：以前用 ProfileHelper.GetProfileByPmcId，它是 .First——Scav 局客户端报上来的是 Scav 档的 id，整条路由抛异常、请求 500、客户端白重试三次
    static IEnumerable<(string id, PmcData pmc)> ProfilesOf(EscortRequest request, SaveServer saves)
    {
        var ids = (request?.Profiles ?? new List<string>()).Where(id => id?.Length == 24 && MongoId.IsValidMongoId(id)).Distinct().ToList();
        if (ids.Count == 0) yield break;
        var all = saves.GetProfiles().Values;
        foreach (var id in ids)
        {
            var want = new MongoId(id);
            var pmc = all.FirstOrDefault(p => p?.CharacterData?.PmcData?.Id == want)?.CharacterData?.PmcData;
            if (pmc != null) yield return (id, pmc);
        }
    }
}

public record EscortBody<T>
{
    [JsonPropertyName("err")] public int Err { get; set; }
    [JsonPropertyName("errmsg")] public string ErrMsg { get; set; }
    [JsonPropertyName("data")] public T Data { get; set; }
}

/// <summary>好友名单里加同伴：挂在 SPT 自己的 /client/friend/list 后面（TypePriority 比 Routers 大一号，SPT 的静态路由把前一个结果从 output 传下来），
/// 干员在 escort_max > 0 时并进去、SORA 在 sora_party 解锁后并进去（10-18）。照 SPT-PitFireTeam 的做法；不注册成聊天机器人（那个按全服配置开关，没法按人门控）。</summary>
[Injectable(TypePriority = OnLoadOrder.Routers + 1)]
public class EscortFriendRouter(JsonUtil jsonUtil, ProfileHelper profileHelper, HttpResponseUtil httpResponse, BotTable bots)
    : StaticRouter(jsonUtil, [
        new RouteAction("/client/friend/list",
            async (url, info, sessionId, output, ct) =>
            {
                var companions = EscortVars.Companions(bots, sessionId.ToString(), profileHelper.GetPmcProfile(sessionId)).ToList();
                if (companions.Count == 0 || string.IsNullOrWhiteSpace(output)) return output ?? httpResponse.NullResponse();
                EscortBody<GetFriendListDataResponse> body = null;
                try { body = jsonUtil.Deserialize<EscortBody<GetFriendListDataResponse>>(output); } catch { }
                var list = body?.Data;
                if (list == null) return output;
                list.Friends ??= new List<UserDialogInfo>();
                foreach (var op in companions)
                    if (!list.Friends.Any(f => f.Id == op.Id)) list.Friends.Add(op);
                return httpResponse.GetBody(list);
            },
            typeof(EmptyRequestData))
    ]);
