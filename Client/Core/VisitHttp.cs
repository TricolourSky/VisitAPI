using System;
using System.Collections;
using System.Threading.Tasks;
using SPT.Common.Http;
using UnityEngine;

namespace VisitAPI.Native;

public static class VisitHttp
{
    // 09-24 审查 M1：发完不等的同步请求排成一条队按顺序发。每条各开一个 Task.Run 的话，同一帧里先后写同一个变量的两条请求
    // 到服务端的先后是随机的，最后落档的值不一定是最后写的那个
    static readonly object Gate = new();
    static Task _tail = Task.CompletedTask;

    public static void Post(string route, string json, string tag)
    {
        lock (Gate)
        {
            _tail = _tail.ContinueWith(_ =>
            {
                try { RequestHandler.PostJson(route, json); }
                catch (Exception e) { Plugin.Log.LogWarning(tag + " server sync failed: " + e.GetBaseException().Message); }
            }, System.Threading.CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    /// 队里此刻排着的写请求全部发完时完成。要读到刚写进服务端的值（比如刚同步的档案变量），先等它再发读请求
    public static Task Flushed() { lock (Gate) return _tail; }

    /// <summary>带请求体、要回包的 POST，在后台线程发、协程里等；tryParse 回 false 或请求抛异常就隔 delay 秒再试，最多 attempts 次。</summary>
    public static IEnumerator Call(string route, string json, Func<string, bool> tryParse, string tag, Action<bool> done, int attempts = 3, float delay = 3f)
    {
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            var task = Task.Run(() => RequestHandler.PostJson(route, json));
            while (!task.IsCompleted) yield return null;
            if (!task.IsFaulted && tryParse(task.Result)) { done(true); yield break; }
            Plugin.Log.LogWarning($"{tag} request failed (attempt {attempt}/{attempts}): " + (task.IsFaulted ? task.Exception?.GetBaseException().Message : "bad response"));
            yield return new WaitForSecondsRealtime(delay);
        }
        done(false);
    }

    /// 开局拉表用的空包 POST：12 次、每次隔 5 秒（服务端还没起来时等它）
    public static IEnumerator Fetch(string route, Func<string, bool> tryParse, string tag, Action<bool> done) =>
        Call(route, "{}", tryParse, tag, done, attempts: 12, delay: 5f);
}
