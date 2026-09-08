using System.Collections.Generic;
using VisitAPI.Dialog;

namespace VisitAPI.Native;

/// <summary>once/first 记号的存取（`&lt;traderId&gt;.seen.json`）。行执行时的打标由 DialogSession 经 LineEffects 分发过来。</summary>
public static class OnceService
{
    static readonly Dictionary<string, DialogStateStore> _stores = new();

    public static DialogStateStore Store(string traderId)
    {
        if (!_stores.TryGetValue(traderId, out var s)) _stores[traderId] = s = new DialogStateStore(DialogFiles.Loader.BaseDir, traderId);
        return s;
    }

    public static void Mark((string trader, string profile, string node, int option) key)
    {
        Store(key.trader).MarkOnce(key.profile, key.node, key.option);
        Plugin.Log.LogDebug($"[once] {key.node}#{key.option} marked");
    }
}
