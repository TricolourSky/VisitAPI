using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Services.Modding.Custom;
using SPTarkov.Server.Core.Utils;
using Path = System.IO.Path;

namespace VisitAPI.Server;

[Injectable(typePriority: OnLoadOrder.PostLoad)]
public class QuestLoader(CustomQuestService questService, ImageRouter images, JsonUtil json, ISptLogger<QuestLoader> log) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var root = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        RegisterImages(root);
        var locales = Files(Path.Combine(root, "db", "locales"))
            .ToDictionary(Path.GetFileNameWithoutExtension, f => Parse<Dictionary<string, string>>(f) ?? new Dictionary<string, string>());
        var ok = 0;
        foreach (var file in Files(Path.Combine(root, "db", "quests")))
        {
            var parsed = Parse<Dictionary<string, Quest>>(file);
            if (parsed == null) continue;
            foreach (var quest in parsed.Values)
            {
                var result = questService.CreateQuest(new NewQuestDetails { NewQuest = quest, Locales = locales });
                if (result.Success) ok++;
                else log.Error($"[VisitAPI] quest {quest.Id} ({Path.GetFileName(file)}): {string.Join("; ", result.Errors ?? new List<string>())}");
            }
        }
        log.Debug($"[VisitAPI] registered {ok} custom quest(s)");
        return Task.CompletedTask;
    }

    // SPT 只自动伺服 SPT_Data/images, 模组的任务图必须自己注册路由;
    // 路由键不带扩展名且按第一个点截断(详见 DEV_NOTES #60 图片路由坑)
    void RegisterImages(string root)
    {
        var dir = Path.Combine(root, "images", "quest", "icon");
        if (!Directory.Exists(dir)) return;
        var n = 0;
        foreach (var file in Directory.GetFiles(dir))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name.Length == 0) continue;
            if (name.Contains('.'))
            {
                log.Warning($"[VisitAPI] 任务图 {Path.GetFileName(file)} 的文件名里有点号，SPT 会把它截断，已跳过");
                continue;
            }
            images.AddRoute($"/files/quest/icon/{name}", file);
            n++;
        }
        if (n > 0) log.Debug($"[VisitAPI] registered {n} quest image(s)");
    }

    T Parse<T>(string file) where T : class
    {
        try { return json.DeserializeFromFile<T>(file); }
        catch (System.Exception e) { log.Error($"[VisitAPI] cannot parse {Path.GetFileName(file)}: {e.Message}"); return null; }
    }

    static string[] Files(string dir) => Directory.Exists(dir) ? Directory.GetFiles(dir, "*.json") : new string[0];
}
