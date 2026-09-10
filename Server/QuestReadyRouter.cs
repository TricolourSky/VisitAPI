using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Ws;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace VisitAPI.Server;

public class QuestReadyRequest : IRequestData
{
    [JsonPropertyName("questId")] public string QuestId { get; set; }
}

/// <summary>
/// 任务 JSON 里的 `"visitapi": { "anyOf", "unlockTraderOnReady", "chapter", "icon", "autoStart", "autoFinish", "dialogOnly", "items", "startAfter" }` 开关。
/// 加新键必须同时改三处：下面的投影、.Where 过滤、ToDictionary 取值——漏掉 .Where 那处，只写了这一个新键的任务会被整条剔出 flags 表（DEV_NOTES #80 洞③）。
/// SPT 的 Quest 模型带 JsonExtensionData，自定义字段原样进库；客户端启动时拉一次 flags 表，
/// 任务变成"可提交"时报一次 ready，服务端按开关解锁该任务的商人并推 UnlockTrader 通知实时点亮头像。见 DEV_NOTES #67。
/// `unlock` 也要下发给客户端：它据此决定报不报 ready（不下发的话，只能退回"这条任务在 .dlg 里出现过才报"，
/// 于是从商人任务列表接的普通任务永远解锁不了商人。DEV_NOTES #80）。
/// </summary>
[Injectable]
public class QuestReadyRouter(JsonUtil jsonUtil, TemplateTable templates, TraderHelper traderHelper, NotificationSendHelper notify, HttpResponseUtil httpResponse)
    : StaticRouter(jsonUtil, [
        new RouteAction("/visitapi/quest/flags",
            async (url, info, sessionId, output, ct) =>
                httpResponse.GetBody(templates.Quests.Values
                    .Select(q => (id: q.Id.ToString(), vx: Visit(q.ExtensionData), notes: Notes(q), subs: Subs(q), story: Story(q.ExtensionData), noCounter: NoCounter(q)))
                    .Select(x => new
                    {
                        x.id, x.notes, anyOf = AnyOf(x.vx), unlock = Flag(x.vx, "unlockTraderOnReady"), chapter = Flag(x.vx, "chapter"),
                        autoStart = Flag(x.vx, "autoStart"), autoFinish = Flag(x.vx, "autoFinish"), dialogOnly = Flag(x.vx, "dialogOnly"), icon = Str(x.vx, "icon"), items = Items(x.vx),
                        startAfter = Str(x.vx, "startAfter"), order = Num(x.vx, "order"), noteLinks = Obj(x.vx, "noteLinks"), x.subs, x.story, x.noCounter,
                        unlockDialogue = StrList(x.vx, "unlockDialogue")
                    })
                    .Where(x => x.anyOf != null || x.unlock || x.chapter || x.autoStart || x.autoFinish || x.dialogOnly || x.icon != null || x.notes != null || x.items.Count > 0 || x.startAfter != null || x.order != null || x.noteLinks != null || x.story || x.noCounter != null || x.unlockDialogue.Count > 0)
                    .ToDictionary(x => x.id, x => new { x.anyOf, x.unlock, x.chapter, x.autoStart, x.autoFinish, x.dialogOnly, x.icon, x.notes, x.items, x.startAfter, x.order, x.noteLinks, x.story, x.noCounter, x.unlockDialogue, subs = x.chapter ? x.subs : null })),
            typeof(QuestReadyRequest)),
        new RouteAction("/visitapi/quest/ready",
            async (url, info, sessionId, output, ct) =>
            {
                var request = (QuestReadyRequest)info;
                if (request.QuestId?.Length == 24 && templates.Quests.TryGetValue(new MongoId(request.QuestId), out var quest)
                    && Flag(Visit(quest.ExtensionData), "unlockTraderOnReady"))
                {
                    traderHelper.SetTraderUnlockedState(quest.TraderId, true, sessionId);
                    await notify.SendMessageAsync(sessionId, new WsProfileChangeEvent
                    {
                        EventIdentifier = new MongoId(), EventType = NotificationEventType.UnlockTrader,
                        Changes = new Dictionary<string, double?> { [quest.TraderId.ToString()] = 1 }
                    });
                }
                return httpResponse.EmptyResponse();
            },
            typeof(QuestReadyRequest))
    ])
{
    /// 任务 JSON 里的 `visitapi` 对象（没有就 null，下面几个取值函数都认 null）
    static JsonElement? Visit(Dictionary<string, object> ext) =>
        ext != null && ext.TryGetValue("visitapi", out var v) && v is JsonElement e && e.ValueKind == JsonValueKind.Object ? e : null;

    static bool Flag(JsonElement? vx, string name) => vx?.TryGetProperty(name, out var p) == true && p.ValueKind == JsonValueKind.True;

    /// `visitapi.anyOf`：true = 任一目标达成即可交（老写法）；数组 = 「二选一组」的目标 id（组内任一达成算组达成，组外照旧全要；09-10）。
    /// 原样下发（bool 或字符串数组），客户端 QuestFlags 两种都认；别的写法当没开（null，flags 表里不占位）
    static object AnyOf(JsonElement? vx) => Flag(vx, "anyOf") ? (object)true
        : vx?.TryGetProperty("anyOf", out var p) == true && p.ValueKind == JsonValueKind.Array ? StrList(vx, "anyOf") : null;

    /// 1.1 任务自带的 `isStoryQuest`（2026-09-07）：1.1 把剧情任务标在这个字段上、名字留空、不进商人的普通任务列表。
    /// 0.16 客户端不认这个字段 → 塔科夫之旅的前置任务顶着空名字出现在 Prapor 的列表里。原样下发，客户端按「剧情任务」隐藏。
    static bool Story(Dictionary<string, object> ext) =>
        ext != null && ext.TryGetValue("isStoryQuest", out var v) && v is JsonElement e && e.ValueKind == JsonValueKind.True;

    static string Str(JsonElement? vx, string name) => vx?.TryGetProperty(name, out var p) == true && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    /// `visitapi.order`：章节显示顺序（G20，小的在前；不碰 .dlg，只是任务 JSON 的新键）
    static double? Num(JsonElement? vx, string name) => vx?.TryGetProperty(name, out var p) == true && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : null;

    /// 章节的子任务表 {子任务id: IsNecessary}——直读任务模板的 AvailableForFinish 里 ConditionType=="Quest" 的条件。
    /// 章节锁没锁都发（B14：客户端不再等章节进任务书才知道谁是谁的子任务）；IsNecessary 给 G19 的主/可选判定。
    static Dictionary<string, bool> Subs(Quest q)
    {
        var result = new Dictionary<string, bool>();
        foreach (var c in q.Conditions?.AvailableForFinish ?? new List<QuestCondition>())
        {
            if (c.ConditionType != "Quest" || c.Target == null) continue;
            var targets = c.Target.IsItem ? new List<string> { c.Target.Item } : c.Target.List ?? new List<string>();
            foreach (var target in targets)
                if (!string.IsNullOrEmpty(target)) result[target] = c.IsNecessary ?? true;
        }
        return result;
    }

    /// `visitapi.items`：相关物品的模板 id 列表（章节屏「相关物品」区，DEV_NOTES #71）
    static List<string> Items(JsonElement? vx) => StrList(vx, "items");

    /// `visitapi.<name>`：字符串数组（items / unlockDialogue）。
    /// unlockDialogue（09-08）：1.1 的 `TraderDialogueUnlock` 奖励在 SPT 的奖励枚举里不存在，QuestPort adapt 把它改写成这个键——
    /// 「这条任务完成后，这些商人的对话（访问）才开放」。客户端按此逐步解锁访问按钮（塔科夫之旅：Ragman/Therapist → Skier → Mechanic → Prapor）。
    static List<string> StrList(JsonElement? vx, string name) =>
        vx?.TryGetProperty(name, out var p) == true && p.ValueKind == JsonValueKind.Array
            ? p.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()).ToList() : new List<string>();

    /// 1.1 条件上的 `showCounter:false`（09-08）：「与 X 交谈」这类 GlobalVariableValue 目标在 1.1 里不画计数/进度条；
    /// 0.16 的 Condition 类没这个字段，从条件 JSON 里捞出来下发条件 id 列表，客户端目标行按它不画进度条。没有就 null（省流量）。
    static List<string> NoCounter(Quest q)
    {
        List<string> ids = null;
        foreach (var c in q.Conditions?.AvailableForFinish ?? new List<QuestCondition>())
            if (c.ExtensionData != null && c.ExtensionData.TryGetValue("showCounter", out var v) && v is JsonElement e && e.ValueKind == JsonValueKind.False)
                (ids ??= new List<string>()).Add(c.Id.ToString());
        return ids;
    }

    /// `visitapi.noteLinks`：{ 日记id: [ { type: item|offer|craft, tpl, quests: [任务id…] } ] }——1.1 日记表（main_quest_notes_list）里
    /// 每条日记挂的相关物品，QuestPort notes 命令从抓包抄进任务 JSON，这里原样下发（2026-09-07）
    static JsonElement? Obj(JsonElement? vx, string name) => vx?.TryGetProperty(name, out var p) == true && p.ValueKind == JsonValueKind.Object ? p : null;

    /// 1.1 格式的 `notes: { Started/Success/Fail: noteId }`，日记正文走 locale 键 `<noteId>`（章节系统 P1，DEV_NOTES #70）。
    /// 2026-09-07：1.1 的目标条件还各自带 `questNoteId`（**目标达成时**解锁的日记，正式版「和 Ragman 交谈」打勾那一刻出的那条），
    /// 0.16 客户端的 Condition 类没这个字段读不到，服务端从条件 JSON 里捞出来并进同一张表，键 `cond:<条件id>`。
    static Dictionary<string, string> Notes(Quest q)
    {
        var ext = q.ExtensionData;
        var notes = ext != null && ext.TryGetValue("notes", out var v) && v is JsonElement e && e.ValueKind == JsonValueKind.Object
            ? e.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String).ToDictionary(p => p.Name, p => p.Value.GetString()) : null;
        foreach (var c in q.Conditions?.AvailableForFinish ?? new List<QuestCondition>())
        {
            if (c.ExtensionData == null || !c.ExtensionData.TryGetValue("questNoteId", out var n) || n is not JsonElement ne || ne.ValueKind != JsonValueKind.String) continue;
            var noteId = ne.GetString();
            if (string.IsNullOrEmpty(noteId)) continue;
            (notes ??= new Dictionary<string, string>())["cond:" + c.Id] = noteId;
        }
        return notes;
    }
}
