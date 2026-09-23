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

[Injectable(typePriority: OnLoadOrder.PostLoad)]
public class UiLocales(LocaleTable locales, ISptLogger<UiLocales> log) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var asm = Assembly.GetExecutingAssembly();
        var packs = new Dictionary<string, Dictionary<string, string>>();
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
        return Task.CompletedTask;
    }
}
