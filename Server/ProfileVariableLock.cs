using System.Collections.Concurrent;
using SPTarkov.Server.Core.Models.Common;

namespace VisitAPI.Server;

/// <summary>09-24 审查 M1：SPT 的 HTTP 监听按请求并发处理，不按会话排队。档案变量表（pmc.Variables）是普通 Dictionary：
/// /visitapi/variable/set（客户端直接同步）和 SaveDialogueState 的重放（DialogueReplayRouter）在对话里一选改了变量组成员时几乎同时到，
/// 并发写同一个 Dictionary 首次新增键时可能把表写坏。改这张表的路由都在这把按会话的锁里改：这两条（改完再重算变量组）、/visitapi/variable/groups、/visitapi/escort/consume、/visitapi/escort/order。</summary>
static class ProfileVariableLock
{
    static readonly ConcurrentDictionary<string, object> Gates = new();

    public static object For(MongoId sessionId) => Gates.GetOrAdd(sessionId.ToString(), _ => new object());
}
