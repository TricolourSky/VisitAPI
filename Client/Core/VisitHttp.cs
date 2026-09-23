using System;
using System.Collections;
using System.Threading.Tasks;
using SPT.Common.Http;
using UnityEngine;

namespace VisitAPI.Native;

public static class VisitHttp
{
    public static void Post(string route, string json, string tag) =>
        Task.Run(() => RequestHandler.PostJson(route, json))
            .ContinueWith(t => { if (t.IsFaulted) Plugin.Log.LogWarning(tag + " server sync failed: " + t.Exception?.GetBaseException().Message); });

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
