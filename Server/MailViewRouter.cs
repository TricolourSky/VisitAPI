using System;
using System.Linq;
using System.Text.Json.Nodes;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace VisitAPI.Server;

public class MailViewRequest : IRequestData { }

/// <summary>09-25 SORA：以前领过的附件在聊天里全不见了；1.1 里领过的附件条照样在，标「(已收取)」（SORA 的 1.1 截图 1.png）。
/// 原因是 SPT 领完附件就把消息的 hasRewards 改成 false（DialogueHelper / InventoryHelper，rewardCollected = true），
/// 0.16.9 客户端只看 hasRewards 决定显不显示附件条（AttachmentMessageView.Show），于是重新登录后领过的附件条整条消失。
/// 这里挂在原生 /client/mail/dialog/view 之后（SPT 同一地址的静态路由依次执行、后一个拿前一个的输出；原生 DialogStaticRouter 优先级 400000，这里 500000），
/// 只改发给客户端的这份，不动存档：rewardCollected 的消息 → hasRewards = true、去掉已空的 items、maxStorageTime = 0。
/// 客户端这样算下来：附件条显示、「(已收取)」亮、领取按钮 / 倒计时 / 「(已过期)」都不亮，也不计入「收取全部」（DisplayRewardStatus 为 false）</summary>
[Injectable(TypePriority = 500000)]
public class MailViewRouter(JsonUtil jsonUtil, ISptLogger<MailViewRouter> log)
    : StaticRouter(jsonUtil, [
        new RouteAction("/client/mail/dialog/view",
            async (url, info, sessionId, output, ct) => Restore(output, log),
            typeof(MailViewRequest))
    ])
{
    static string Restore(string output, ISptLogger<MailViewRouter> log)
    {
        if (string.IsNullOrEmpty(output)) return output;
        try
        {
            var root = JsonNode.Parse(output);
            if (root?["data"]?["messages"] is not JsonArray messages) return output;
            var changed = 0;
            foreach (var m in messages.OfType<JsonObject>())
            {
                var collected = m["rewardCollected"] is JsonValue rc && rc.TryGetValue(out bool c) && c;
                var has = m["hasRewards"] is JsonValue hr && hr.TryGetValue(out bool h) && h;
                if (!collected || has) continue;
                m["hasRewards"] = true;
                m.Remove("items");
                m["maxStorageTime"] = 0;
                changed++;
            }
            if (changed == 0) return output;
            return root.ToJsonString();
        }
        catch (Exception e)
        {
            log.Warning("[VisitAPI] mail view: could not mark collected attachments, sending the native view: " + e.Message);
            return output;
        }
    }
}
