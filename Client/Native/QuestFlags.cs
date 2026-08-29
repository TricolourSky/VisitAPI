using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EFT.Quests;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;

namespace VisitAPI.Native;

/// <summary>
/// 任务 JSON 里 VisitAPI 自己的开关（`visitapi.*` + 1.1 格式的 `notes`），启动时从 /visitapi/quest/flags 拉一次（服务端没起来就隔 5 秒再试）。
/// anyOf = 目标任一达成即可交；unlock = 变成可提交时解锁发布它的商人；chapter = 这条任务是章节；icon = 章节图标 URL；notes = 状态→日记 id；
/// autoStart = 一变成可接就自动接、autoFinish = 一达成就自动交（章节链，DEV_NOTES #71/#74）；dialogOnly = 接/交只能走对话；items = 相关物品的模板 id。
/// </summary>
public static class QuestFlags
{
    public class Entry { public bool AnyOf, Unlock, Chapter, AutoStart, AutoFinish, DialogOnly; public string Icon; public Dictionary<string, string> Notes = new(); public List<string> Items = new(); }

    static readonly Dictionary<string, Entry> _byQuest = new();
    static readonly Dictionary<string, string> _chapterOf = new();        // 子任务 → 章节
    static readonly Dictionary<string, List<string>> _subsOf = new();    // 章节 → 子任务

    public static Entry Get(string questId) { lock (_byQuest) return _byQuest.TryGetValue(questId ?? "", out var e) ? e : null; }
    public static bool AnyOf(string id) => Get(id)?.AnyOf == true;
    public static bool Unlock(string id) => Get(id)?.Unlock == true;
    public static bool IsChapter(string id) => Get(id)?.Chapter == true;
    public static bool AutoStart(string id) => Get(id)?.AutoStart == true;
    public static bool AutoFinish(string id) => Get(id)?.AutoFinish == true;
    public static bool DialogOnly(string id) => Get(id)?.DialogOnly == true;

    /// 剧情任务 = 章节本身 + 它目标里指到的子任务；章节任务一进任务书（ChapterChain 的钩子）就登记，列表过滤 / 横幅 / 自动链都按这张表查
    public static string ChapterOf(string subId) { lock (_byQuest) return subId != null && _chapterOf.TryGetValue(subId, out var c) ? c : null; }
    public static List<string> SubsOf(string chapterId) { lock (_byQuest) return _subsOf.TryGetValue(chapterId ?? "", out var l) ? l : new List<string>(); }
    public static bool IsStory(string id) => IsChapter(id) || ChapterOf(id) != null;
    public static void MarkStory(Quest chapter)
    {
        if (chapter?.Template == null || !IsChapter(chapter.Id) || !chapter.Template.Conditions.TryGetValue(EQuestStatus.AvailableForFinish, out var cc)) return;
        var subs = cc.OfType<ConditionQuest>().Select(c => c.target).Where(t => !string.IsNullOrEmpty(t)).ToList();
        lock (_byQuest) { _subsOf[chapter.Id] = subs; foreach (var s in subs) _chapterOf[s] = chapter.Id; }
    }

    public static void Prefetch() => Plugin.Instance.StartCoroutine(Fetch());

    // 解析在主线程做（出错有日志，不会被吞）；成功时若任务书已经在了，把章节补登记一遍——flags 迟到的话登录时那轮扫描是空跑的
    static IEnumerator Fetch()
    {
        for (var attempt = 1; attempt <= 12; attempt++)
        {
            var task = Task.Run(() => RequestHandler.PostJson("/visitapi/quest/flags", "{}"));
            while (!task.IsCompleted) yield return null;
            if (!task.IsFaulted && TryParse(task.Result))
            {
                var book = ChapterChain.Controller?.Quests; if (book != null) foreach (var q in book) MarkStory(q);
                yield break;
            }
            Plugin.Log.LogWarning($"[flags] fetch failed (attempt {attempt}/12): " + (task.IsFaulted ? task.Exception?.GetBaseException().Message : "bad response"));
            yield return new UnityEngine.WaitForSecondsRealtime(5f);
        }
    }

    static bool TryParse(string body)
    {
        try
        {
            if (!(JObject.Parse(body)["data"] is JObject data)) return false;
            lock (_byQuest)
                foreach (var p in data.Properties())
                {
                    var e = new Entry { AnyOf = On(p.Value, "anyOf"), Unlock = On(p.Value, "unlock"), Chapter = On(p.Value, "chapter"), AutoStart = On(p.Value, "autoStart"), AutoFinish = On(p.Value, "autoFinish"), DialogOnly = On(p.Value, "dialogOnly"), Icon = p.Value["icon"]?.Value<string>() };
                    if (p.Value["notes"] is JObject notes) foreach (var n in notes.Properties()) e.Notes[n.Name] = n.Value.Value<string>();
                    if (p.Value["items"] is JArray items) e.Items = items.Select(x => x.Value<string>()).Where(s => !string.IsNullOrEmpty(s)).ToList();
                    _byQuest[p.Name] = e;
                }
            Plugin.Log.LogDebug($"[flags] {_byQuest.Count} quest(s) with VisitAPI flags");
            // 章节图标先拉下来：横幅可能比剧情页先出现，那时没缓存就只能显示默认对勾
            lock (_byQuest)
                foreach (var e in _byQuest.Values)
                    if (!string.IsNullOrEmpty(e.Icon)) ChapterUI.ChapterImages.Preload(e.Icon);
            return true;
        }
        catch (System.Exception ex) { Plugin.Log.LogWarning("[flags] parse failed: " + ex.Message); return false; }
    }

    static bool On(JToken t, string name) => t[name]?.Value<bool>() == true;
}
