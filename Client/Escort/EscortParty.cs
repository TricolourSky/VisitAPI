using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EFT;
using EFT.UI;
using EFT.UI.Chat;
using EFT.UI.Matchmaker;
using HarmonyLib;
using JsonType;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;

namespace VisitAPI.Native;

/// <summary>10-16 SORA：「让玩家自己邀请护卫进队」——主菜单左下角的好友 / 队伍面板。照 SPT-PitFireTeam 的路数，但真正的组队流程一点不碰
/// （SPT 的组队开局没实现、Fika 另有一套）：服务端把干员并进好友名单（EscortFriendRouter，上限 escort_max 决定几个），
/// 玩家在空位上点「邀请进队」时这里截住，不发服务端，只在面板的可见名单里加一行、把订单（面板上的人数）发给服务端；右键「移出队伍」反过来。
/// 面板每次打开按服务端的订单把行补回来。行只进 BaseGroupPanel 的可见名单，不进 MatchmakerPlayersController.GroupPlayers，
/// 所以准备页 / 开局流程看不到它们，也不会因为没有形象数据报空。
/// 10-24：问服务端那一下后台发、协程里等（在主线程同步等回包的话，连远程 Fika 主机时每开一次邀请窗菜单卡一个来回）。
/// 对话 / 控制台放开 escort_max、sora_party 的那一刻也顺手重拉一次，开邀请窗时名单已经是新的。</summary>
public static class EscortParty
{
    class Member { public string Aid, Id, Nick; }

    static readonly Dictionary<string, Member> _ops = new();        // aid → 干员（服务端名单）
    static readonly Dictionary<string, RaidPlayer> _rows = new();   // aid → 面板上的那一行
    static BaseGroupPanel _panel;
    static SocialNetwork _social;
    static int _max;
    static readonly List<string> _picked = new();                    // 10-17：服务端记着的「点名邀请的是哪几个」（aid）
    static bool _refreshing, _refreshAgain;
    static int _lastCount;                                           // 最近一次问到的订单人数（读不到记 0）
    static readonly MongoID LimitVar = Vars.Id("escort_max"), SoraVar = Vars.Id("sora_party");

    /// 23:07 实机：好友名单是登录时拉一次就缓存的，之后对话 / 控制台把 escort_max 放开了客户端也不知道。
    /// 服务端名单里有干员而本地好友里没有时，照 SocialNetwork 自己登录时的路子重拉一遍（ISocial.GetFriendsList → 它的回调 CG_method_13 更新列表）
    static void RefreshFriends()
    {
        try
        {
            if (_social == null || _max <= 0 || _ops.Count == 0) return;
            if (_social.FriendsList != null && _ops.Keys.All(aid => _social.FriendsList.Any(f => f?.AccountId == aid))) return;
            if (_social._session is not ISocial session) return;
            session.GetFriendsList(r => { try { if (r.Succeed) _social.CG_method_13(r); } catch (Exception e) { Plugin.WarnOnce("escort-friends-apply", "[escort] friends list refresh could not be applied: " + e.Message); } });
        }
        catch (Exception e) { Plugin.WarnOnce("escort-friends", "[escort] could not refresh the friends list: " + e.Message); }
    }

    public static bool IsEscortAid(string aid) => aid != null && _ops.ContainsKey(aid);

    /// 问服务端：位子数、名单、点名；回包到了交给 done（当前订单的人数；读不到给 0）。同一时刻只跑一个：
    /// 跑着的时候再来一次，就让跑着的那个完了以后补跑一次，自己等它跑完再拿最新的结果交给 done（PanelShown.Restore 靠这个拿人数，不能空手返回）
    static IEnumerator Refresh(Action<int> done)
    {
        if (_refreshing)
        {
            _refreshAgain = true;
            while (_refreshing) yield return null;
            done?.Invoke(_lastCount);
            yield break;
        }
        _refreshing = true;
        try
        {
            do
            {
                _refreshAgain = false;
                var task = Task.Run(() => RequestHandler.PostJson("/visitapi/escort/party", "{}"));
                while (!task.IsCompleted) yield return null;
                var count = 0;
                try
                {
                    if (task.IsFaulted) throw task.Exception.GetBaseException();
                    count = Apply(task.Result);
                }
                catch (Exception e) { Plugin.WarnOnce("escort-party", "[escort] could not read the escort party from the server: " + e.GetBaseException().Message); }
                _lastCount = count;
                done?.Invoke(count);
                done = null;
            } while (_refreshAgain);
        }
        finally { _refreshing = false; }
    }

    static int Apply(string json)
    {
        var d = JObject.Parse(json)["data"] as JObject;
        if (d == null) return 0;
        _max = d["max"]?.Value<int>() ?? 0;
        _ops.Clear();
        foreach (var m in d["members"] as JArray ?? new JArray())
        {
            var aid = m["aid"]?.Value<string>();
            if (!string.IsNullOrEmpty(aid)) _ops[aid] = new Member { Aid = aid, Id = m["id"]?.Value<string>(), Nick = m["nick"]?.Value<string>() };
        }
        _picked.Clear();
        foreach (var x in d["picked"] as JArray ?? new JArray()) { var aid = x.Value<string>(); if (!string.IsNullOrEmpty(aid)) _picked.Add(aid); }
        return d["count"]?.Value<int>() ?? 0;
    }

    static RaidPlayer Row(Member m) => new(new GroupPlayer
    {
        AccountId = m.Aid, Id = m.Id, IsReady = true,
        Info = new PlayerInfo { Nickname = m.Nick ?? Loc.Pick("队友", "Teammate"), Side = EPlayerSide.Savage, Level = 30, MemberCategory = EMemberCategory.Default, SelectedMemberCategory = EMemberCategory.Default }
    });

    static void Add(string aid)
    {
        if (_panel == null || _rows.ContainsKey(aid) || !_ops.TryGetValue(aid, out var m)) return;
        var r = Row(m);
        _rows[aid] = r;
        _panel.AddVisibleGroupPlayers(r);
    }

    static void Drop(string aid)
    {
        if (!_rows.TryGetValue(aid, out var r)) return;
        _rows.Remove(aid);
        try { _panel?.RemoveVisibleGroupPlayers(r); } catch (Exception e) { Plugin.WarnOnce("escort-row", "[escort] could not remove an escort row: " + e.Message); }
    }

    /// 订单 = 面板上是哪几个（服务端按位记，开局刷的就是被点名的那几个、叫那几个名字）
    static void Sync() => VisitHttp.Post("/visitapi/escort/order",
        "{\"members\":[" + string.Join(",", _rows.Keys.Select(a => "\"" + a + "\"")) + "]}", "[escort]");

    /// 面板打开：记住实例，问过服务端以后按它的订单把行补回来
    [HarmonyPatch(typeof(BaseGroupPanel), "Show", typeof(IMatchmakerController), typeof(SocialNetwork))]
    public static class PanelShown
    {
        static void Postfix(BaseGroupPanel __instance, SocialNetwork socialNetwork)
        {
            try
            {
                _panel = __instance;
                _social = socialNetwork ?? _social;
                _rows.Clear();
                Plugin.Instance.StartCoroutine(Restore(__instance));
            }
            catch (Exception e) { Plugin.WarnOnce("escort-panel", "[escort] could not show escorts in the group panel: " + e.Message); }
        }

        static IEnumerator Restore(BaseGroupPanel panel)
        {
            var count = 0;
            yield return Refresh(c => count = c);
            if (_panel != panel || _max <= 0) yield break;   // 等回包的工夫面板换了一个
            var restore = _picked.Count > 0 ? _picked.Where(_ops.ContainsKey).ToList() : _ops.Keys.OrderBy(a => a).Take(count).ToList();
            foreach (var aid in restore.Take(Math.Min(count, _max))) Add(aid);
            RefreshFriends();
        }
    }

    /// 点空位弹邀请窗：面板只在进主菜单时 Show 一次，这里再问一次服务端（上限可能刚放开），好友里缺干员就重拉。
    /// 窗先照常开，回包到了名单跟着变；对话里刚放开上限那种情况 VarChanged 已经提前拉过了
    [HarmonyPatch(typeof(UI.Matchmaker.Group.FriendListInvitePlayerPanel), nameof(UI.Matchmaker.Group.FriendListInvitePlayerPanel.Show))]
    public static class InviteWindow
    {
        static void Prefix() => Plugin.Instance.StartCoroutine(Refresh(_ => RefreshFriends()));
    }

    /// 对话 `set: escort_max=N` / `set: sora_party=1`（或控制台 visit_setvar）一落到本地档案就重拉名单，不等玩家开邀请窗
    [HarmonyPatch(typeof(ProfileVariablesStorage), nameof(ProfileVariablesStorage.SetVariableValue))]
    public static class VarChanged
    {
        static void Postfix(MongoID variableId)
        {
            if (!variableId.Equals(LimitVar) && !variableId.Equals(SoraVar)) return;
            try { Plugin.Instance.StartCoroutine(AfterSync()); }
            catch (Exception e) { Plugin.WarnOnce("escort-var", "[escort] could not refresh the escort party after an unlock: " + e.Message); }
        }

        /// 本地档案先改（就是这一下）、同步服务端的请求后排：控制台走 Vars.Set，对话是引擎先赋值、OnExecuteLine 再 Vars.Sync，都在同一帧里。
        /// 当场就问服务端会抢在同步前面、读到旧上限，所以先等一帧让同步排上队，再等队里的写请求发完
        static IEnumerator AfterSync()
        {
            yield return null;
            var flushed = VisitHttp.Flushed();
            while (!flushed.IsCompleted) yield return null;
            yield return Refresh(_ => RefreshFriends());
        }
    }

    /// SendInvite / RemovePlayerFromGroup 声明在泛型基类 BaseMatchmakerController&lt;RaidSettings&gt; 上：HarmonyPatch 特性只找子类自己声明的方法
    /// （23:10 日志「Undefined target method」），改用 TargetMethod 顺着继承链找
    static System.Reflection.MethodBase Inherited(string name, params Type[] args) =>
        AccessTools.Method(typeof(MatchmakerPlayersController), name, args)
        ?? throw new MissingMethodException(typeof(MatchmakerPlayersController).FullName, name);

    /// 「邀请进队」：目标是干员就不发服务端，面板加一行 + 订单
    [HarmonyPatch]
    public static class Invite
    {
        static System.Reflection.MethodBase TargetMethod() => Inherited("SendInvite", typeof(string), typeof(bool), typeof(Action));

        static bool Prefix(string accountId, Action callback)
        {
            if (!IsEscortAid(accountId)) return true;
            try
            {
                if (_rows.Count < _max) { Add(accountId); Sync(); }
                else Plugin.WarnOnce("escort-full", $"[escort] invite ignored: the party already has {_rows.Count}/{_max} escorts");
                callback?.Invoke();
            }
            catch (Exception e) { Plugin.WarnOnce("escort-invite", "[escort] invite failed: " + e.Message); }
            return false;
        }
    }

    /// 「移出队伍」：同上反过来
    [HarmonyPatch]
    public static class Remove
    {
        static System.Reflection.MethodBase TargetMethod() => Inherited("RemovePlayerFromGroup", typeof(string), typeof(Action));

        static bool Prefix(string playerAid, Action callback)
        {
            if (!IsEscortAid(playerAid)) return true;
            try { Drop(playerAid); Sync(); callback?.Invoke(); }
            catch (Exception e) { Plugin.WarnOnce("escort-remove", "[escort] remove failed: " + e.Message); }
            return false;
        }
    }

    /// 干员的右键菜单：没进队只给「邀请进队」（没满才给），进了队只给「移出队伍」
    [HarmonyPatch(typeof(RaidGroupContextInteractions), nameof(RaidGroupContextInteractions.IsActive))]
    public static class GroupMenu
    {
        static bool Prefix(RaidGroupContextInteractions __instance, ERaidPlayerButton button, ref bool __result)
        {
            var aid = __instance?._selectedPlayer?.AccountId;
            if (!IsEscortAid(aid)) return true;
            __result = _rows.ContainsKey(aid) ? button == ERaidPlayerButton.RemovePlayer : button == ERaidPlayerButton.InviteInGroup && _rows.Count < _max;
            return false;
        }
    }

    /// 好友列表里干员的右键菜单：什么都不给（没有资料页，也不聊天）
    [HarmonyPatch(typeof(ChatMemberContextInteractions), nameof(ChatMemberContextInteractions.IsActive))]
    public static class FriendMenu
    {
        static bool Prefix(ChatMemberContextInteractions __instance, ref bool __result)
        {
            if (!IsEscortAid(__instance?._selectedMember?.AccountId)) return true;
            __result = false;
            return false;
        }
    }
}
