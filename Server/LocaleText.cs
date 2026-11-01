using System.Collections.Generic;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils.Json;

namespace VisitAPI.Server;

static class LocaleText
{
    static readonly string[] TextLangs = ["en", "ch", "ru"];

    /// <summary>key 在 en / ch / ru 任一语言里有正文（不是空白）。notKey = true 时正文就是 key 本身也算没有（StoryQuestMail 这么判，InviteRouter 一直没这一条）。
    /// 每取一次 lazy.Value，SPT 都要把整份语言文件重新解析、重跑全部变换器。</summary>
    public static bool Has(LocaleTable locales, string key, bool notKey = false)
    {
        if (locales?.Global == null) return false;
        foreach (var lang in TextLangs)
        {
            if (!locales.Global.TryGetValue(lang, out var lazy)) continue;
            try
            {
                var table = lazy?.Value;
                if (table != null && table.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text) && !(notKey && text == key)) return true;
            }
            catch { }
        }
        return false;
    }

    /// 给一种语言挂一个变换器：entries 里语言表还没有的键补进去，已有的不动（同一个键谁先挂谁赢）
    public static void Add(LazyLoad<GlobalLocaleDictionary> lazy, Dictionary<string, string> entries) =>
        lazy.AddTransformer(data => { if (data != null) foreach (var (k, v) in entries) data.TryAdd(k, v); return data; });

    /// 每种语言各挂一个：有这种语言的用它，没有的用 fallback；都没有或是空表就不挂
    public static void AddAll(LocaleTable locales, Dictionary<string, Dictionary<string, string>> byLang, Dictionary<string, string> fallback)
    {
        foreach (var (lang, lazy) in locales.Global)
        {
            var entries = byLang.TryGetValue(lang, out var own) ? own : fallback;
            if (entries is { Count: > 0 }) Add(lazy, entries);
        }
    }
}
