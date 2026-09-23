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

    public class SendLocalisedPatch : AbstractPatch
    {
        protected override MethodBase GetTargetMethod() =>
            typeof(MailSendService).GetMethod(nameof(MailSendService.SendLocalisedNpcMessageToPlayer));

        [PatchPrefix]
        public static bool Prefix(string messageLocaleId, IEnumerable<Item> items) => !Suppress(messageLocaleId, items != null && items.Any());
    }
}
