using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Commerce;

namespace VisitAPI.Server;

[Injectable(typePriority: OnLoadOrder.PostLoad)]
public class StoryQuestMail(TemplateTable templates, LocaleTable locales, ISptLogger<StoryQuestMail> log) : IOnLoad
{
    static TemplateTable _templates;
    static LocaleTable _locales;
    static ISptLogger<StoryQuestMail> _log;
    static readonly Regex QuestKey = new("^([0-9a-f]{24}) (description|startedMessageText|successMessageText|failMessageText)$", RegexOptions.Compiled);

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        _templates = templates; _locales = locales; _log = log;
        var patch = new SendLocalisedPatch();
        try
        {
            patch.Enable();
            if (!patch.IsActive) log.Error("[VisitAPI] story-quest mail filter did NOT arm (IsActive=false after Enable)");
        }
        catch (Exception e) { log.Error("[VisitAPI] story-quest mail filter failed to arm: " + e); }
        return Task.CompletedTask;
    }

    internal static bool Suppress(string messageLocaleId, bool hasItems)
    {
        try
        {
            if (_templates == null || string.IsNullOrEmpty(messageLocaleId)) return false;
            if (messageLocaleId == QuestLoader.RewardMailKey) return !hasItems;   // 1.1 通用奖励文案：没附件不寄
            var m = QuestKey.Match(messageLocaleId);
            if (!m.Success) return false;                                                                      // 不是任务键，照寄
            if (!_templates.Quests.TryGetValue(new MongoId(m.Groups[1].Value), out var quest)) return false;   // 任务不在库里，照寄
            var story = quest.ExtensionData != null && quest.ExtensionData.TryGetValue("isStoryQuest", out var v) && v is JsonElement e && e.ValueKind == JsonValueKind.True;
            // 只压剧情任务（isStoryQuest）没附件的信（#145）；别的任务——包括自己章节里没标剧情的——照 SPT 原样，只有「没正文且没物品」才不寄。
            // 09-22 曾按「我们加载的所有任务」压，作者章节里只给经验的任务完成信永远寄不到；09-23 SORA 定：改回。编辑器 mail_dropped 同步只报剧情任务
            return story ? !hasItems : !HasText(messageLocaleId) && !hasItems;
        }
        catch (Exception ex) { _log?.Error("[VisitAPI] npc mail filter error (passthrough): " + ex.Message); return false; }
    }

    static readonly string[] TextLocales = { "en", "ch", "ru" };

    static bool HasText(string key)
    {
        if (_locales?.Global == null) return false;
        foreach (var lang in TextLocales)
        {
            if (!_locales.Global.TryGetValue(lang, out var lazy)) continue;
            try
            {
                var table = lazy?.Value;
                if (table != null && table.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text) && text != key) return true;
            }
            catch {  }
        }
        return false;
    }

    /// 1.1 的隐藏商人：Player Trader（67f7af56）是剧情任务的隐藏归属者，1.1 / SPT 5 里它名下的任务由服务端 StartStorylineQuest 静默开始，
    /// 不寄「任务开始」信、聊天里从不露面（SORA 09-24：「我怎么从来没见过这个 Player Trader」）。4.1 上我们的自动链走正常接任务，
    /// 服务端会以它的名义寄信，聊天里就冒出一个「Player Trader」——它的信一律不以商人名义寄：有附件就改成系统消息送附件，没附件直接不寄
    static readonly HashSet<string> HiddenSenders = new(StringComparer.OrdinalIgnoreCase) { "67f7af56c117b6140af2a607" };

    public class SendLocalisedPatch : AbstractPatch
    {
        protected override MethodBase GetTargetMethod() =>
            typeof(MailSendService).GetMethod(nameof(MailSendService.SendLocalisedNpcMessageToPlayer));

        [PatchPrefix]
        public static bool Prefix(MailSendService __instance, MongoId sessionId, MongoId? trader, MessageType messageType, string messageLocaleId, IEnumerable<Item> items, long? maxStorageTimeSeconds)
        {
            var list = items?.ToList();
            var hasItems = list != null && list.Count > 0;
            if (Suppress(messageLocaleId, hasItems)) return false;
            if (!trader.HasValue || !HiddenSenders.Contains(trader.Value.ToString())) return true;
            if (!hasItems) return false;
            try { __instance.SendLocalisedSystemMessageToPlayer(sessionId, messageLocaleId, list, null, maxStorageTimeSeconds); }
            catch (Exception e) { _log?.Error("[VisitAPI] Failed to convert hidden trader's mail into a system message, sending it as is: " + e.Message); return true; }
            return false;
        }
    }
}
