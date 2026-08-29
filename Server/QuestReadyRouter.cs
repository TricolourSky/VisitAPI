using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Models.Common;
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
/// 任务 JSON 里的 `"visitapi": { "anyOf", "unlockTraderOnReady", "chapter", "icon", "autoStart", "autoFinish", "dialogOnly", "items" }` 开关。
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
                    .Select(q => (id: q.Id.ToString(), vx: Visit(q.ExtensionData), notes: Notes(q.ExtensionData)))
                    .Select(x => new
                    {
                        x.id, x.notes, anyOf = Flag(x.vx, "anyOf"), unlock = Flag(x.vx, "unlockTraderOnReady"), chapter = Flag(x.vx, "chapter"),
                        autoStart = Flag(x.vx, "autoStart"), autoFinish = Flag(x.vx, "autoFinish"), dialogOnly = Flag(x.vx, "dialogOnly"), icon = Str(x.vx, "icon"), items = Items(x.vx)
                    })
                    .Where(x => x.anyOf || x.unlock || x.chapter || x.autoStart || x.autoFinish || x.dialogOnly || x.icon != null || x.notes != null || x.items.Count > 0)
                    .ToDictionary(x => x.id, x => new { x.anyOf, x.unlock, x.chapter, x.autoStart, x.autoFinish, x.dialogOnly, x.icon, x.notes, x.items })),
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

    static string Str(JsonElement? vx, string name) => vx?.TryGetProperty(name, out var p) == true && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    /// `visitapi.items`：相关物品的模板 id 列表（章节屏「相关物品」区，DEV_NOTES #71）
    static List<string> Items(JsonElement? vx) =>
        vx?.TryGetProperty("items", out var p) == true && p.ValueKind == JsonValueKind.Array
            ? p.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()).ToList() : new List<string>();

    /// 1.1 格式的 `notes: { Started/Success/Fail: noteId }`，日记正文走 locale 键 `<noteId>`（章节系统 P1，DEV_NOTES #70）
    static Dictionary<string, string> Notes(Dictionary<string, object> ext) =>
        ext != null && ext.TryGetValue("notes", out var v) && v is JsonElement e && e.ValueKind == JsonValueKind.Object
            ? e.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String).ToDictionary(p => p.Name, p => p.Value.GetString()) : null;
}
