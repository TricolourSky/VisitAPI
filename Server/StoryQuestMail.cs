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

/// <summary>
/// 2026-09-07：1.1 剧情任务（`isStoryQuest`）自动接下/交掉时，SPT 会照普通任务的路子给玩家寄一封「任务开始/完成」邮件，
/// 文案键是 `<任务id> description` / `startedMessageText` / `successMessageText`——1.1 里这些键**本来就是空串**（剧情任务不走邮件，
/// 走对话和日记），我们的本地化合并又只收非空键，于是客户端把键名当正文显示成一串 id（SORA 新档实机：Prapor 两封乱码邮件）。
/// 处置：在 MailSendService.SendLocalisedNpcMessageToPlayer 前面拦一下——剧情任务 + 文案键在任何语言里都没有正文 → 不寄。
/// 其它任务、有正文的邮件一律不碰。SPT 4 服务端的补丁走 SPTarkov.Reflection（Harmony），这是本项目服务端第一处补丁。
/// </summary>
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
        // SPT 4.1 的 PatchManager：PatcherName 只是名字，Harmony 实例要到 EnablePatches() 里才建；单独调 EnablePatch 会因为
        // _harmony 为空照样抛"without setting a PatcherName"（09-07 实机两次，查过 server-csharp 源码）。正路是 AddPatch → EnablePatches。
        // 09-07 实机：PatchManager.AddPatch + EnablePatches 不报错但补丁没挂上（IsActive=false、TargetMethod=null，邮件照发），
        // 它的 EnablePatches 只认自己扫出来的补丁。AbstractPatch 自带 Harmony 实例，直接 Enable() 最省事、可验证。
        var patch = new SendLocalisedPatch();
        try
        {
            patch.Enable();
            if (patch.IsActive) log.Info($"[VisitAPI] story-quest mail filter armed (target={patch.TargetMethod?.DeclaringType?.Name}.{patch.TargetMethod?.Name})");
            else log.Error("[VisitAPI] story-quest mail filter did NOT arm (IsActive=false after Enable)");
        }
        catch (Exception e) { log.Error("[VisitAPI] story-quest mail filter failed to arm: " + e); }
        return Task.CompletedTask;
    }

    /// 任务键 + 没有正文 + **邮件里没有物品** → 压掉。
    /// 09-07 终审：SPT 4.1.5 的 `SendLocalisedNpcMessageToPlayer(sessionId, trader, messageType, messageLocaleId, IEnumerable&lt;Item&gt; items, …)`
    /// ——任务的物品奖励就是随这封邮件发的（塔科夫之旅有两条任务奖 30 万 / 100 万卢布，文案照样是空的）。整封压掉 = 奖励一起没了。
    /// 所以带物品的一律放行（正文空着就空着，客户端只是把键名显示成正文，物品照收）；只有「没正文且没物品」才压。
    internal static bool Suppress(string messageLocaleId, bool hasItems)
    {
        try
        {
            if (_templates == null || string.IsNullOrEmpty(messageLocaleId)) return false;
            var m = QuestKey.Match(messageLocaleId);
            if (!m.Success) { _log?.Debug($"[VisitAPI] npc mail passthrough (not a quest key): {messageLocaleId}"); return false; }
            if (!_templates.Quests.TryGetValue(new MongoId(m.Groups[1].Value), out var quest)) { _log?.Debug($"[VisitAPI] npc mail passthrough (quest not in db): {messageLocaleId}"); return false; }
            var story = quest.ExtensionData != null && quest.ExtensionData.TryGetValue("isStoryQuest", out var v) && v is JsonElement e && e.ValueKind == JsonValueKind.True;
            var hasText = HasText(messageLocaleId);
            // 09-07 实机：章节闭包里的隐藏机制件（notDisplayedQuest，isStoryQuest=false）同样没文案、同样寄乱码——
            // 一封正文是键名的邮件在任何情况下都不该寄，判据收敛成「没正文就不寄」，story 只留在日志里
            var suppress = !hasText && !hasItems;
            _log?.Info($"[VisitAPI] npc mail {(suppress ? "suppressed" : "passthrough")}: {messageLocaleId} story={story} hasText={hasText} hasItems={hasItems}");
            return suppress;
        }
        catch (Exception ex) { _log?.Error("[VisitAPI] npc mail filter error (passthrough): " + ex.Message); return false; }
    }

    /// 只查这几种语言：`lazy.Value` 会把整张语言表加载常驻，遍历全部 20+ 种等于第一封邮件就把所有语言表都拉进内存（09-07 终审）。
    /// 剧情任务的文案键在 1.1 里是「所有语言都没有」，查 en / ch / ru 三张足够判定。
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
            catch { /* 某个语言表加载失败不影响判定 */ }
        }
        return false;
    }

    public class SendLocalisedPatch : AbstractPatch
    {
        protected override MethodBase GetTargetMethod() =>
            typeof(MailSendService).GetMethod(nameof(MailSendService.SendLocalisedNpcMessageToPlayer));

        // 参数名 messageLocaleId / items 与 SPT 4.1.5 的签名逐字对过（反射核实，09-07）
        [PatchPrefix]
        public static bool Prefix(string messageLocaleId, IEnumerable<Item> items) => !Suppress(messageLocaleId, items != null && items.Any());
    }
}
