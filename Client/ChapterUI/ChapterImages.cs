using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using SPT.Common.Http;
using UnityEngine;
using UnityEngine.UI;
using VisitAPI.Native;

namespace VisitAPI.ChapterUI
{
    /// <summary>章节图标/横幅从服务端 `/files/quest/icon/...` 拉，按 URL 缓存成 Sprite。下载在线程池、回灌在协程（主线程）；
    /// 同一 URL 下载中时后来的 Image 只排队不重复请求。B4：失败按 10s→20s→…→300s 退避重试，不再跟着刷屏节奏打请求风暴。</summary>
    public static class ChapterImages
    {
        static readonly Dictionary<string, Sprite> _cache = new();
        static readonly Dictionary<string, List<Image>> _pending = new();
        static readonly Dictionary<string, (float until, float delay)> _backoff = new();

        /// 已经下载过的图（横幅要用章节图标；没缓存就先不给，下次开章节屏会拉）
        public static Sprite Cached(string url) => !string.IsNullOrEmpty(url) && _cache.TryGetValue(url, out var s) ? s : null;

        /// 只下载不贴图：章节横幅可能比剧情页先弹（flags 一到手就预拉全部章节图标，否则横幅只能退回默认对勾，实机踩过）
        public static void Preload(string url) => Start(url, null);

        public static void Apply(Image target, string url)
        {
            if (target == null || string.IsNullOrEmpty(url)) return;
            if (_cache.TryGetValue(url, out var s)) { target.sprite = s; target.enabled = true; return; }
            Start(url, target);
        }

        static void Start(string url, Image target)
        {
            if (string.IsNullOrEmpty(url) || _cache.ContainsKey(url)) return;
            if (_pending.TryGetValue(url, out var waiting)) { if (target != null) waiting.Add(target); return; }
            if (_backoff.TryGetValue(url, out var b) && Time.unscaledTime < b.until) return;   // 退避期内不发
            _pending[url] = target != null ? new List<Image> { target } : new List<Image>();
            Plugin.Instance.StartCoroutine(Fetch(url));
        }

        static IEnumerator Fetch(string url)
        {
            var task = Task.Run(() => RequestHandler.GetData(url));
            while (!task.IsCompleted) yield return null;
            var sprite = task.IsFaulted || task.Result == null || task.Result.Length == 0 ? null : VisitArt.Decode(task.Result);
            if (sprite != null) { _cache[url] = sprite; _backoff.Remove(url); }
            else
            {
                var delay = _backoff.TryGetValue(url, out var b) ? Mathf.Min(b.delay * 2f, 300f) : 10f;
                _backoff[url] = (Time.unscaledTime + delay, delay);
                Plugin.Log.LogWarning($"[chapter] image failed ({delay:0}s backoff): " + url + (task.IsFaulted ? " (" + task.Exception?.GetBaseException().Message + ")" : ""));
            }
            var targets = _pending[url]; _pending.Remove(url);
            foreach (var t in targets) if (t != null) { t.sprite = sprite; t.enabled = sprite != null; }
        }
    }
}
