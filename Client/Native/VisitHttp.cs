using System.Threading.Tasks;
using SPT.Common.Http;

namespace VisitAPI.Native;

/// <summary>"打完就走"的 POST（好感 / 变量 / 任务达成上报都是这种）：只记失败。回调里不碰 Unity 对象，留在线程池没关系。</summary>
public static class VisitHttp
{
    public static void Post(string route, string json, string tag) =>
        Task.Run(() => RequestHandler.PostJson(route, json))
            .ContinueWith(t => { if (t.IsFaulted) Plugin.Log.LogWarning(tag + " server sync failed: " + t.Exception?.GetBaseException().Message); });
}
