using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils;

namespace VisitAPI.Server;

[Injectable]
public class QuestReadyRouter(JsonUtil jsonUtil, TemplateTable templates, HttpResponseUtil httpResponse)
    : StaticRouter(jsonUtil, [
        new RouteAction("/visitapi/quest/flags",
            async (url, info, sessionId, output, ct) =>
            {
                var result = new Dictionary<string, Dictionary<string, object>>();
                foreach (var q in templates.Quests.Values)
                    if (FlagsOf(q, templates) is { } flags) result.Add(q.Id.ToString(), flags);
                return httpResponse.GetBody(result);
            },
            typeof(EmptyRequestData)),
        // 10-16 深夜 SORA：包里注册的商人 id 名单——客户端 LockedTraderHide 凭它让没解锁的剧情商人不出现在交易界面（原版是灰卡；Abyss 要等「深渊的频道」解锁那一刻才露面）
        new RouteAction("/visitapi/traders",
            async (url, info, sessionId, output, ct) => httpResponse.GetBody(TraderLoader.Registered.Select(id => id.ToString()).ToList()),
            typeof(EmptyRequestData))
    ])
{
    /// <summary>一条任务的 flags，字段按下发顺序逐个放：null 不放（SPT 序列化时本来就略过 null），false / 空表照放；有一项「有内容」才收进结果，否则给 null。</summary>
    static Dictionary<string, object> FlagsOf(Quest q, TemplateTable templates)
    {
        var ext = q.ExtensionData;
        var vx = Ext.Visit(ext);
        var chapter = Ext.Flag(vx, "chapter");
        var subs = chapter ? Subs(q) : null;   // 子任务只有章节下发
        var missingSubs = subs == null ? null : MissingSubs(subs, templates);
        var finishers = Finishers(q);
        var startOnItems = Ext.StrList(vx, "startOnItems");
        var f = new FlagSet();
        f.Opt("anyOf", AnyOf(vx));
        f.Bool("chapter", chapter);
        f.Bool("autoStart", Ext.Flag(vx, "autoStart"));
        f.Bool("autoFinish", Ext.Flag(vx, "autoFinish"));
        f.Bool("dialogOnly", Ext.Flag(vx, "dialogOnly"));
        f.Opt("icon", Ext.Str(vx, "icon"));
        f.Opt("notes", Notes(q));
        f.List("items", Ext.StrList(vx, "items"));
        f.Opt("startAfter", StartAfter(vx));
        f.Opt("order", Ext.Num(vx, "order"));
        f.Opt("noteLinks", Ext.Obj(vx, "noteLinks"));
        f.Bool("story", Ext.IsTrue(ext, "isStoryQuest"));
        f.Opt("noCounter", CondIds(q, "showCounter", JsonValueKind.False));
        f.List("unlockDialogue", Ext.StrList(vx, "unlockDialogue"));
        f.Opt("talkTo", TalkTo(q, templates));
        f.List("unlockLocations", Ext.StrList(vx, "unlockLocations"));
        f.Opt("call", Call(ext));
        f.Opt("mail", Mail(ext));
        f.Bool("hidden", Ext.IsTrue(ext, "notDisplayedQuest"));
        f.Opt("setVariables", Ext.Obj(vx, "setVariables"));
        // 10-01：内容包里的任务（战局内交了由 StoryRaidRewards 在结算时统一发奖励）；客户端凭它在局内交任务时不再当场发一份
        f.Bool("pack", QuestLoader.Loaded.Contains(q.Id.ToString()));
        f.Opt("finishers", finishers.Count > 0 ? finishers : null);
        f.Opt("subs", subs, counts: false);
        f.Opt("missingSubs", missingSubs?.Count > 0 ? missingSubs : null, counts: false);
        // 10-05：拿到其中任意一件就接（1.1 AutoStart 桶里「读到就开」的物品，adapt 搬进 visitapi.startOnItems）
        f.Opt("startOnItems", startOnItems.Count > 0 ? startOnItems : null);
        f.Opt("startItemRules", StartItemRules(startOnItems), counts: false);
        f.Opt("rewardXp", RewardXp(q));
        // 10-15：父目标完成时按完成打勾的可选子目标（目标条件上写 doneWithParent: true，见客户端 ChapterStates.Row）
        f.Opt("doneWithParent", CondIds(q, "doneWithParent", JsonValueKind.True));
        return f.Any ? f.Fields : null;
    }

    sealed class FlagSet
    {
        public readonly Dictionary<string, object> Fields = new();
        public bool Any;   // 有一项「有内容」：true / 非 null / 非空表

        /// 不是 null 才放；counts = false 的只下发、不算「有内容」（subs / missingSubs / startItemRules，跟着 chapter / startOnItems 走）
        public void Opt(string key, object value, bool counts = true)
        {
            if (value == null) return;
            Fields[key] = value;
            Any |= counts;
        }

        public void Bool(string key, bool value) { Fields[key] = value; Any |= value; }

        public void List(string key, List<string> value) { Fields[key] = value; Any |= value.Count > 0; }
    }

    static object AnyOf(JsonElement? vx) => Ext.Flag(vx, "anyOf") ? (object)true
        : vx?.TryGetProperty("anyOf", out var p) == true && p.ValueKind == JsonValueKind.Array ? Ext.StrList(vx, "anyOf") : null;

    /// startAfter：一个任务 id（字符串）或多个（数组，任一完成即可开）——09-24 陨落星辰要么枪匠对话那条桥接任务、要么踩到森林坠机；原样下发，客户端两种都认
    static object StartAfter(JsonElement? vx)
    {
        var s = Ext.Str(vx, "startAfter");
        if (s != null) return s;
        var list = Ext.StrList(vx, "startAfter");
        return list.Count > 0 ? list : null;
    }

    static Dictionary<string, bool> Subs(Quest q)
    {
        var result = new Dictionary<string, bool>();
        foreach (var c in q.Conditions?.AvailableForFinish ?? new List<QuestCondition>())
            foreach (var target in Ext.QuestCondTargets(c)) result[target] = c.IsNecessary ?? true;
        return result;
    }

    /// 章节点名但本机根本没有定义的子任务（门票缺的 44 条）。09-24 客户端按章节顺序开任务时要分清「前一条还没出现」和「前一条本机没有」：前者等，后者不拦
    static HashSet<string> _knownQuests;
    static List<string> MissingSubs(Dictionary<string, bool> subs, TemplateTable templates)
    {
        var known = _knownQuests ??= new HashSet<string>(templates.Quests.Values.Select(t => t.Id.ToString()));
        return subs.Keys.Where(id => !known.Contains(id)).ToList();
    }

    /// 10-06（SORA：蓝焰、意外证人做完没弹经验）：1.1 每完成一步，把这一步完成奖励里标了 isImportant 的逐条弹「剧情通关奖励：」
    /// （QuestNotificationManager.NotifyReward，1.1.0 反汇编核过）。0.16 的 QuestReward 没有 isImportant，这里替客户端把要弹的经验加好带下去；
    /// 各包里标了 isImportant 的奖励目前只有经验（蓝焰 / 意外证人的经验都挂在最后一步上）
    static double? RewardXp(Quest q)
    {
        if (q.Rewards == null || !q.Rewards.TryGetValue("Success", out var list) || list == null) return null;
        var xp = list.Where(r => r?.Type == RewardType.Experience && Ext.IsTrue(r.ExtensionData, "isImportant")).Sum(r => r.Value ?? 0);
        return xp > 0 ? xp : null;
    }

    static List<string> Finishers(Quest q)
    {
        var list = new List<string>();
        foreach (var c in q.Conditions?.AvailableForFinish ?? new List<QuestCondition>())
            if (Ext.IsTrue(c.ExtensionData, "isFinisher")) list.AddRange(Ext.QuestCondTargets(c));
        return list;
    }

    /// 10-06：开章物品在 1.1 里的「个人可见」规矩（ItemLoader 登记包里物品时记下）；客户端 ItemStart.Visible 在刷任务物品时照着判断这名玩家看不看得到
    static Dictionary<string, object> StartItemRules(List<string> tpls)
    {
        Dictionary<string, object> result = null;
        foreach (var tpl in tpls)
        {
            if (!ItemLoader.PersonalRules.TryGetValue(tpl, out var r)) continue;
            (result ??= new Dictionary<string, object>())[tpl] = new
            {
                quests = r.Quests.Select(q => new { id = q.Id, started = q.Started, completed = q.Completed, failed = q.Failed }).ToList(),
                items = r.Items.Select(i => new { tpl = i.Tpl, completed = i.Completed }).ToList(),
                bear = r.Bear, usec = r.Usec, savage = r.Savage, level = r.Level,
            };
        }
        return result;
    }

    static Dictionary<string, string> _dialogTrader;
    static string TalkTo(Quest q, TemplateTable templates)
    {
        var dialogueId = q.DialogueId?.ToString();
        if (string.IsNullOrEmpty(dialogueId)) return null;
        var map = _dialogTrader;
        if (map == null)
        {
            map = new Dictionary<string, string>();
            foreach (var el in templates.Dialogue?.Elements ?? new List<TraderDialogElement>()) map[el.Id.ToString()] = el.MainTrader.ToString();
            _dialogTrader = map;
        }
        return map.TryGetValue(dialogueId, out var trader) && trader.Length == 24 ? trader : null;
    }

    static string Call(Dictionary<string, object> ext)
    {
        if (Ext.MailSettings(ext) is not { } e) return null;
        var trader = Ext.Str(e, "dialogueTraderId") ?? Ext.Str(e, "fromTraderId");
        return trader?.Length == 24 ? trader : null;
    }

    /// <summary>1.1 的邀请信设置（任务可接时该商人往聊天里发的信）原样下发：from = 发信商人，entry = InLobby / InRaid / ViaRadio / ViaNotebook，
    /// dialogue / dialogueTrader = 按下按钮要开的对话与商人，text = 信正文的文案键。邀请信本身由客户端发现任务可接后请求 /visitapi/mail/invite 寄出。</summary>
    static object Mail(Dictionary<string, object> ext)
    {
        if (Ext.MailSettings(ext) is not { } e) return null;
        var from = Ext.Str(e, "fromTraderId");
        if (from?.Length != 24) return null;
        return new
        {
            from,
            entry = Ext.Str(e, "entryPoint") ?? "InLobby",
            dialogue = Ext.Str(e, "dialogueId"),
            dialogueTrader = Ext.Str(e, "dialogueTraderId") ?? from,
            text = Ext.Str(e, "whileAvailableMessageText"),
        };
    }

    /// 目标条件上扩展字段 key 是这种值的条件 id：showCounter: false（noCounter）、10-15 doneWithParent: true（父目标完成时客户端按完成打勾而不是「跳过」）；一个都没有就 null
    static List<string> CondIds(Quest q, string key, JsonValueKind kind)
    {
        List<string> ids = null;
        foreach (var c in q.Conditions?.AvailableForFinish ?? new List<QuestCondition>())
            if (Ext.Get(c.ExtensionData, key, kind) != null) (ids ??= new List<string>()).Add(c.Id.ToString());
        return ids;
    }

    static Dictionary<string, string> Notes(Quest q)
    {
        var notes = Ext.Get(q.ExtensionData, "notes", JsonValueKind.Object) is { } e
            ? e.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String).ToDictionary(p => p.Name, p => p.Value.GetString()) : null;
        foreach (var c in q.Conditions?.AvailableForFinish ?? new List<QuestCondition>())
        {
            if (Ext.Get(c.ExtensionData, "questNoteId", JsonValueKind.String)?.GetString() is not { Length: > 0 } noteId) continue;
            (notes ??= new Dictionary<string, string>())["cond:" + c.Id] = noteId;
        }
        return notes;
    }
}
