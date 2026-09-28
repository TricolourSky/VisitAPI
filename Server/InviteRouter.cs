using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Commerce;
using SPTarkov.Server.Core.Utils;

namespace VisitAPI.Server;

public class InviteRequest : IRequestData
{
    [JsonPropertyName("questId")] public string QuestId { get; set; }
}

/// <summary>1.1 的「对话邀请」：任务一可接，指定商人往聊天里发一封信（正文 = 任务的 whileAvailableMessageText）。1.1 里这封信是客户端按任务模板自己生成的，
/// 0.16.9 客户端没有这套，所以由插件客户端在发现任务可接时来请求，这里以该商人的名义寄一封普通信；每条任务一个档案只寄一次，寄过记在档案 ExtensionData 的
/// visitapi.invites 里（和 visitapi.read 分开存，互不覆盖）。</summary>
[Injectable]
public class InviteRouter(JsonUtil jsonUtil, ProfileHelper profiles, SaveServer saveServer, TemplateTable templates, LocaleTable locales, MailSendService mail, HttpResponseUtil httpResponse, ISptLogger<InviteRouter> log)
    : StaticRouter(jsonUtil, [
        new RouteAction("/visitapi/mail/invite",
            async (url, info, sessionId, output, ct) =>
            {
                var questId = ((InviteRequest)info)?.QuestId;
                var profile = profiles.GetFullProfile(sessionId);
                if (profile == null || string.IsNullOrEmpty(questId) || !MongoId.IsValidMongoId(questId)) return httpResponse.GetBody(new { sent = false, reason = "bad request" });
                if (!templates.Quests.TryGetValue(new MongoId(questId), out var quest)) return httpResponse.GetBody(new { sent = false, reason = "unknown quest" });
                if (quest.ExtensionData == null || !quest.ExtensionData.TryGetValue("mailSettings", out var v) || v is not JsonElement e || e.ValueKind != JsonValueKind.Object
                    || !(e.TryGetProperty("isEnabled", out var on) && on.ValueKind == JsonValueKind.True))
                    return httpResponse.GetBody(new { sent = false, reason = "no mail settings" });
                var from = e.TryGetProperty("fromTraderId", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null;
                if (from?.Length != 24) return httpResponse.GetBody(new { sent = false, reason = "no sender" });
                var textKey = questId + " whileAvailableMessageText";
                if (!HasText(locales, textKey)) { log.Warning($"[VisitAPI] Invite mail {questId}: locale table has no {textKey}, not sending"); return httpResponse.GetBody(new { sent = false, reason = "no text" }); }
                var sent = false;
                await Locked(sessionId, ct, async () =>
                {
                    var done = Load(profile);
                    var had = done.Contains(questId);
                    // 09-24 审查 H1：0.16.9 允许右键删除商人对话，删了之后「寄过」的记号还在、信却没了，
                    // Kerman 这类只能从聊天里点「回复」的邀请就再也打不开。所以寄过也要看聊天里那封还在不在，不在就补寄
                    if (had && HasInviteMessage(profile, from, textKey)) return;
                    mail.SendLocalisedNpcMessageToPlayer(sessionId, new MongoId(from), MessageType.NpcTraderMessage, textKey, null);
                    done.Add(questId);
                    Store(profile, done);
                    await saveServer.SaveProfileAsync(sessionId, ct);
                    sent = true;
                });
                return httpResponse.GetBody(new { sent });
            },
            typeof(InviteRequest)),
        new RouteAction("/visitapi/mail/invite/list",
            async (url, info, sessionId, output, ct) =>
            {
                var profile = profiles.GetFullProfile(sessionId);
                return httpResponse.GetBody(profile == null ? new List<string>() : Load(profile).OrderBy(x => x, StringComparer.Ordinal).ToList());
            },
            typeof(InviteRequest))
    ])
{
    const string Key = "visitapi.invites";
    static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    static async Task Locked(string sessionId, CancellationToken ct, Func<Task> body)
    {
        var gate = Locks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try { await body(); }
        finally { gate.Release(); }
    }

    static bool HasText(LocaleTable locales, string key)
    {
        foreach (var lang in new[] { "en", "ch", "ru" })
        {
            if (!locales.Global.TryGetValue(lang, out var lazy)) continue;
            try { var table = lazy?.Value; if (table != null && table.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text)) return true; }
            catch { }
        }
        return false;
    }

    /// SPT 寄本地化 NPC 信时把文案键记在消息的 TemplateId 上（MailSendService.SendLocalisedNpcMessageToPlayer），对话按商人 id 存在 DialogueRecords 里；
    /// 玩家删掉对话 = 整条 DialogueRecords[商人] 被移除
    static bool HasInviteMessage(SptProfile profile, string from, string textKey)
    {
        if (profile.DialogueRecords == null || !MongoId.IsValidMongoId(from)) return false;
        if (!profile.DialogueRecords.TryGetValue(new MongoId(from), out var dialog) || dialog?.Messages == null) return false;
        return dialog.Messages.Any(m => m != null && string.Equals(m.TemplateId, textKey, StringComparison.Ordinal));
    }

    static HashSet<string> Load(SptProfile profile)
    {
        var s = new HashSet<string>(StringComparer.Ordinal);
        var ext = profile.ExtensionData;
        if (ext == null || !ext.TryGetValue(Key, out var v)) return s;
        if (v is JsonElement je && je.ValueKind == JsonValueKind.Array) { foreach (var x in je.EnumerateArray()) if (x.ValueKind == JsonValueKind.String) s.Add(x.GetString()); }
        else if (v is IEnumerable<string> list) foreach (var x in list) s.Add(x);
        else if (v is IEnumerable<object> objs) foreach (var x in objs) if (x is string str) s.Add(str);
        return s;
    }

    static void Store(SptProfile profile, HashSet<string> s)
    {
        profile.ExtensionData ??= new Dictionary<string, object>();
        profile.ExtensionData[Key] = s.OrderBy(x => x, StringComparer.Ordinal).ToList();
    }
}
