using System;
using System.Collections;
using System.Threading.Tasks;
using SPT.Common.Http;
using UnityEngine;

namespace VisitAPI.Native;

/// <summary>"打完就走"的 POST（好感 / 变量上报都是这种）：只记失败。回调里不碰 Unity 对象，留在线程池没关系。</summary>
public static class VisitHttp
{
    public static void Post(string route, string json, string tag) =>
        Task.Run(() => RequestHandler.PostJson(route, json))
            .ContinueWith(t => { if (t.IsFaulted) Plugin.Log.LogWarning(tag + " server sync failed: " + t.Exception?.GetBaseException().Message); });

    /// <summary>带重试的启动拉取（flags 表 / 已读表都是这种）：最多 12 次、间隔 5 秒；请求在线程池、解析在主线程（出错有日志不会被吞）。
    /// tryParse 返回真即成功，done(true/false) 在主线程回调。体检第二轮：原 QuestFlags / ReadState 各写一套一模一样的循环。</summary>
    public static IEnumerator Fetch(string route, Func<string, bool> tryParse, string tag, Action<bool> done)
    {
        for (var attempt = 1; attempt <= 12; attempt++)
        {
            var task = Task.Run(() => RequestHandler.PostJson(route, "{}"));
            while (!task.IsCompleted) yield return null;
            if (!task.IsFaulted && tryParse(task.Result)) { done(true); yield break; }
            Plugin.Log.LogWarning($"{tag} fetch failed (attempt {attempt}/12): " + (task.IsFaulted ? task.Exception?.GetBaseException().Message : "bad response"));
            yield return new WaitForSecondsRealtime(5f);
        }
        done(false);
    }
}
