using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using EFT;
using SPT.Common.Http;
using UnityEngine;
using UnityEngine.UI;
using VisitAPI.Native;

namespace VisitAPI.ChapterUI
{
    public static class ChapterImages
    {
        static readonly Dictionary<string, Sprite> _cache = new();
        static readonly Dictionary<string, List<Image>> _pending = new();
        static readonly Dictionary<string, (float until, float delay)> _backoff = new();

        public static Sprite Cached(string url) => !string.IsNullOrEmpty(url) && _cache.TryGetValue(url, out var s) ? s : null;

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
            if (_backoff.TryGetValue(url, out var b) && Time.unscaledTime < b.until) return;
            _pending[url] = target != null ? new List<Image> { target } : new List<Image>();
            Plugin.Instance.StartCoroutine(Fetch(url));
        }

        static IEnumerator Fetch(string url)
        {
            Sprite sprite = null;
            string why = null;
            try
            {
                var loader = VisitBackend.Session as IImageLoader;
                if (loader != null)
                {
                    var t = loader.LoadTextureMain(url);
                    while (!t.IsCompleted) yield return null;
                    // 会话被拆时引擎的任务是「取消」的：IsFaulted 挡不住，读 Result 直接抛（10-29）
                    if (t.Status == TaskStatus.RanToCompletion && t.Result != null)
                        sprite = Sprite.Create(t.Result, new Rect(0f, 0f, t.Result.width, t.Result.height), new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect);
                    else why = t.IsFaulted ? t.Exception?.GetBaseException().Message : t.IsCanceled ? "engine loader cancelled" : "engine loader returned null";
                }
                if (sprite == null)
                {
                    var task = Task.Run(() => RequestHandler.GetData(url));
                    while (!task.IsCompleted) yield return null;
                    sprite = task.Status != TaskStatus.RanToCompletion || task.Result == null || task.Result.Length == 0 ? null : VisitArt.Decode(task.Result);
                    if (sprite == null && task.IsFaulted) why = task.Exception?.GetBaseException().Message;
                }
                if (sprite != null) { _cache[url] = sprite; _backoff.Remove(url); }
                else
                {
                    var delay = _backoff.TryGetValue(url, out var b) ? Mathf.Min(b.delay * 2f, 300f) : 10f;
                    _backoff[url] = (Time.unscaledTime + delay, delay);
                    Plugin.Log.LogWarning($"[chapter] image failed ({delay:0}s backoff): " + url + (why != null ? " (" + why + ")" : ""));
                }
            }
            finally
            {
                // 哪一步抛了也要把等待表清掉，不然这张图本会话再也不拉（Start 看到 _pending 里有就只排队）
                if (_pending.TryGetValue(url, out var targets))
                {
                    _pending.Remove(url);
                    foreach (var t in targets) if (t != null) { t.sprite = sprite; t.enabled = sprite != null; }
                }
            }
        }
    }
}
