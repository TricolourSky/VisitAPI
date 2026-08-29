using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using SPT.Common.Http;
using UnityEngine;
using UnityEngine.UI;
using VisitAPI.Native;

namespace VisitAPI.ChapterUI
{
    /// <summary>章节图标/横幅从服务端 `/files/quest/icon/...` 拉，按 URL 缓存成 Sprite。下载在线程池、回灌在协程（主线程）；同一个 URL 正在下载时后来的 Image 只排队，不重复请求。</summary>
    public static class ChapterImages
    {
        static readonly Dictionary<string, Sprite> _cache = new();
        static readonly Dictionary<string, List<Image>> _pending = new();

        /// 已经下载过的图（章节横幅要用章节图标；没缓存就先不给，下次打开章节屏会拉）
        public static Sprite Cached(string url) => !string.IsNullOrEmpty(url) && _cache.TryGetValue(url, out var s) ? s : null;

        /// 只下载不贴图：章节横幅在剧情页第一次打开之前就可能弹出来，那时候缓存是空的，
        /// 图标位置只能退回默认的黄色对勾（实机踩过）。flags 一到手就把所有章节图标先拉下来。
        public static void Preload(string url)
        {
            if (string.IsNullOrEmpty(url) || _cache.ContainsKey(url) || _pending.ContainsKey(url)) return;
            _pending[url] = new List<Image>();
            Plugin.Instance.StartCoroutine(Fetch(url));
        }

        public static void Apply(Image target, string url)
        {
            if (target == null || string.IsNullOrEmpty(url)) return;
            if (_cache.TryGetValue(url, out var s)) { target.sprite = s; target.enabled = true; return; }
            if (_pending.TryGetValue(url, out var waiting)) { waiting.Add(target); return; }
            _pending[url] = new List<Image> { target };
            Plugin.Instance.StartCoroutine(Fetch(url));
        }

        static IEnumerator Fetch(string url)
        {
            var task = Task.Run(() => RequestHandler.GetData(url));
            while (!task.IsCompleted) yield return null;
            var sprite = task.IsFaulted || task.Result == null || task.Result.Length == 0 ? null : VisitArt.Decode(task.Result);
            if (sprite != null) _cache[url] = sprite;   // 失败不记缓存，下次开屏重试
            else Plugin.Log.LogWarning("[chapter] image failed: " + url + (task.IsFaulted ? " (" + task.Exception?.GetBaseException().Message + ")" : ""));
            var targets = _pending[url]; _pending.Remove(url);
            foreach (var t in targets) if (t != null) { t.sprite = sprite; t.enabled = sprite != null; }
        }
    }
}
