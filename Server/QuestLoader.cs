using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Services.Modding.Custom;
using SPTarkov.Server.Core.Utils;
using Path = System.IO.Path;

namespace VisitAPI.Server;

[Injectable(typePriority: OnLoadOrder.PostLoad)]
public class QuestLoader(CustomQuestService questService, ImageRouter images, JsonUtil json, LocaleTable localeTable, ISptLogger<QuestLoader> log) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var root = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        RegisterImages(root);
        var locales = Files(Path.Combine(root, "db", "locales"))
            .ToDictionary(Path.GetFileNameWithoutExtension, f => Parse<Dictionary<string, string>>(f) ?? new Dictionary<string, string>());
        var ok = 0;
        var all = new Dictionary<string, Quest>();
        foreach (var file in Files(Path.Combine(root, "db", "quests")))
        {
            var parsed = Parse<Dictionary<string, Quest>>(file);
            if (parsed == null) continue;
            foreach (var quest in parsed.Values)
            {
                var result = questService.CreateQuest(new NewQuestDetails { NewQuest = quest, Locales = locales });
                if (result.Success) { ok++; all[quest.Id.ToString()] = quest; }
                else log.Error($"[VisitAPI] quest {quest.Id} ({Path.GetFileName(file)}): {string.Join("; ", result.Errors ?? new List<string>())}");
            }
        }
        log.Debug($"[VisitAPI] registered {ok} custom quest(s)");
        NameStoryQuests(all);
        return Task.CompletedTask;
    }

    /// <summary>1.1 的剧情子任务没有名字（`&lt;id&gt; name` 在 1.1 里就是空串，1.1 客户端按剧情任务另行处理），0.16 客户端拿不到文案就把键名当名字显示——
    /// 物品提示「6895bf1e… name 的任务所需物品已在战局中被找到」（SORA 09-08 实机）。这里给章节闭包里每条没名字的任务补一个名字 = 它所属章节的名字
    ///（每种语言各取该语言的章节名），只补缺、不改已有。闭包 = 章节 AvailableForFinish 点名的子任务 + 它们条件里点名的任务（隐藏机制件）。</summary>
    void NameStoryQuests(Dictionary<string, Quest> all)
    {
        var chapterOf = new Dictionary<string, string>();
        foreach (var (id, q) in all)
        {
            if (!(q.ExtensionData != null && q.ExtensionData.TryGetValue("visitapi", out var v) && v is JsonElement vx && vx.ValueKind == JsonValueKind.Object
                  && vx.TryGetProperty("chapter", out var ch) && ch.ValueKind == JsonValueKind.True)) continue;
            var queue = new Queue<string>(QuestTargets(q, all));
            while (queue.Count > 0)
            {
                var sub = queue.Dequeue();
                if (sub == id || chapterOf.ContainsKey(sub) || !all.TryGetValue(sub, out var sq)) continue;
                chapterOf[sub] = id;
                foreach (var t in QuestTargets(sq, all)) queue.Enqueue(t);
            }
        }
        if (chapterOf.Count == 0) return;
        foreach (var (_, lazy) in localeTable.Global)
            lazy.AddTransformer(data =>
            {
                if (data == null) return data;
                foreach (var (sub, chapter) in chapterOf)
                {
                    var key = sub + " name";
                    if (data.TryGetValue(key, out var existing) && !string.IsNullOrWhiteSpace(existing)) continue;
                    if (data.TryGetValue(chapter + " name", out var name) && !string.IsNullOrWhiteSpace(name)) data[key] = name;
                }
                return data;
            });
        log.Debug($"[VisitAPI] story quest names: {chapterOf.Count} quest(s) fall back to their chapter's name");
    }

    /// 任务各条件桶里 conditionType=Quest 点名的任务 id（只认本次注册过的）
    static IEnumerable<string> QuestTargets(Quest q, Dictionary<string, Quest> all)
    {
        var buckets = new[] { q.Conditions?.AvailableForStart, q.Conditions?.AvailableForFinish, q.Conditions?.Fail };
        foreach (var bucket in buckets)
            foreach (var c in bucket ?? new List<QuestCondition>())
            {
                if (c.ConditionType != "Quest" || c.Target == null) continue;
                var targets = c.Target.IsItem ? new List<string> { c.Target.Item } : c.Target.List ?? new List<string>();
                foreach (var t in targets) if (!string.IsNullOrEmpty(t) && all.ContainsKey(t)) yield return t;
            }
    }

    // SPT 只自动伺服 SPT_Data/images, 模组的任务图必须自己注册路由;
    // 路由键不带扩展名且按第一个点截断(详见 DEV_NOTES #60 图片路由坑)
    // 2026-09-07：1.1 的章节图标走 /files/quest/chapters_icon/<id>.png（章节 `icon` 字段），横幅走 /files/quest/icon/<id>.png（`image` 字段），
    // 所以 images\quest\ 下每个子目录都按同名路由登记：images\quest\<子目录>\<名>.<ext> → /files/quest/<子目录>/<名>
    void RegisterImages(string root)
    {
        var questDir = Path.Combine(root, "images", "quest");
        if (!Directory.Exists(questDir)) return;
        var n = 0;
        foreach (var dir in Directory.GetDirectories(questDir))
        {
            var sub = Path.GetFileName(dir);
            foreach (var file in Directory.GetFiles(dir))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (name.Length == 0) continue;
                if (name.Contains('.'))
                {
                    log.Warning($"[VisitAPI] 任务图 {sub}/{Path.GetFileName(file)} 的文件名里有点号，SPT 会把它截断，已跳过");
                    continue;
                }
                images.AddRoute($"/files/quest/{sub}/{name}", file);
                n++;
            }
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
