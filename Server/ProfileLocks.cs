using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace VisitAPI.Server;

/// <summary>按会话排队改档案 ExtensionData 再存盘：/visitapi/quest/read、/visitapi/quest/objectives、/visitapi/mail/invite 共用一把
/// （10-17 审查 E11：两边改的是同一个 profile.ExtensionData，又都在锁里 SaveProfileAsync 序列化整份档案）。
/// 不可重入：body 里别再 RunAsync。改 pmc.Variables 的是另一把 ProfileVariableLock（同步 lock，不跨 await），两把从不嵌套。</summary>
static class ProfileLocks
{
    static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();

    public static async Task RunAsync(string sessionId, CancellationToken ct, Func<Task> body)
    {
        var gate = Gates.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try { await body(); }
        finally { gate.Release(); }
    }
}
