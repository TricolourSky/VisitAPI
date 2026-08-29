using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace VisitAPI.Server;

/// <summary>剧情页自己的界面文案（`UI/MainQuests/*`：「剧情」「主要目标」「相关物品」…）随服务端 DLL 走：嵌入资源 ui/ch.json、ui/en.json，
/// 启动时塞进全局文案表（和 CustomQuestService.AddQuestLocales 同一条路：LazyLoad.AddTransformer + TryAdd）。中文表给 ch，其它语言一律兜底英文。
/// 这些是框架的字不是作者的剧本文案——发布包不带 db/locales、作者一条任务都没写时也必须有。DEV_NOTES #76。</summary>
[Injectable(typePriority: OnLoadOrder.PostLoad)]
public class UiLocales(LocaleTable locales, ISptLogger<UiLocales> log) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var asm = Assembly.GetExecutingAssembly();
        var packs = new Dictionary<string, Dictionary<string, string>>();   // <root>.ui.<lang>.json → lang
        foreach (var name in asm.GetManifestResourceNames())
        {
            var parts = name.Split('.');
            if (parts.Length < 3 || parts[^1] != "json" || parts[^3] != "ui") continue;
            using var stream = asm.GetManifestResourceStream(name);
            var entries = stream == null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
            if (entries != null && entries.Count > 0) packs[parts[^2]] = entries;
        }
        if (!packs.TryGetValue("en", out var fallback)) { log.Warning("[VisitAPI] ui locale: embedded ui/en.json missing"); return Task.CompletedTask; }
        foreach (var (lang, lazy) in locales.Global)
        {
            var entries = packs.TryGetValue(lang, out var own) ? own : fallback;
            lazy.AddTransformer(dict => { if (dict != null) foreach (var (k, v) in entries) dict.TryAdd(k, v); return dict; });
        }
        log.Debug($"[VisitAPI] ui locale: {fallback.Count} key(s) for {locales.Global.Count} language(s)");
        return Task.CompletedTask;
    }
}
