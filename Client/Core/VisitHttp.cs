using System;
using System.Collections;
using System.Threading.Tasks;
using SPT.Common.Http;
using UnityEngine;

namespace VisitAPI.Native;

public static class VisitHttp
{
    // 09-24 审查 M1：发完不等的同步请求排成一条队按顺序发。以前每条各开一个 Task.Run，同一帧里先后写同一个变量的两条请求
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
