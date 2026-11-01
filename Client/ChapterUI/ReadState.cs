using System.Collections.Generic;
using System.Linq;
using EFT.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VisitAPI.Native;

namespace VisitAPI.ChapterUI
{
    public static class ReadState
    {
        static readonly HashSet<string> _read = new();
        static bool _fetching;
        static string _profile;   // 记录属于哪个档案；换档案就清掉重拉（10-24，不清的话登出换档还拿上一个档案的已读）

        public static bool IsRead(string id) => _read.Contains(id);
        public static bool AnyUnread(IEnumerable<string> ids) => ids.Any(id => !_read.Contains(id));
        public static int CountUnread(IEnumerable<string> ids) => ids.Count(id => !_read.Contains(id));

        /// 标成已读；真有新标上的才发服务端，返回有没有新标上的（10-24，旧 B20：调用方只在有新增时重算未读角标）
        public static bool MarkRead(IEnumerable<string> ids)
        {
            var fresh = ids.Where(_read.Add).ToList();
            if (fresh.Count == 0) return false;
            Post(fresh);
            return true;
        }

        static void Post(List<string> ids) =>
            VisitHttp.Post("/visitapi/quest/read", "{\"ids\":" + JsonConvert.SerializeObject(ids) + "}", "[chapter/read]");

        public static void Sync()
        {
            var profile = VisitBackend.Profile?.Id;
            if (profile != null && profile != _profile) { _profile = profile; _read.Clear(); _fetching = false; }
            if (_fetching) return;
            _fetching = true;
            Plugin.Instance.StartCoroutine(VisitHttp.Fetch("/visitapi/quest/read/list", TryParse, "[chapter/read]", ok =>
            {
                if (ok) return;
                _fetching = false;
                Plugin.Log.LogWarning("[chapter/read] giving up for now - will retry when tasks screen opens");
            }));
        }

        static bool TryParse(string body)
        {
            try
            {
                if (JObject.Parse(body)["data"] is not JArray arr) return false;
                foreach (var t in arr) { var id = t.Value<string>(); if (!string.IsNullOrEmpty(id)) _read.Add(id); }
                return true;
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("[chapter/read] parse failed: " + e.Message); return false; }
        }

        public static void OnHover(GameObject area, System.Action onEnter)
        {
            if (area == null) return;
            if (area.GetComponent<UnityEngine.UI.Graphic>() == null) area.AddComponent<NonDrawingGraphic>().raycastTarget = true;
            var h = area.GetComponent<HoverReadTrigger>() ?? area.AddComponent<HoverReadTrigger>();
            h.Enter = onEnter;
        }
    }
}
