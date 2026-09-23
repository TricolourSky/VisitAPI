using System.Collections.Generic;
using System.IO;
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
        static readonly string LegacyPath = Path.Combine(BepInEx.Paths.ConfigPath, "VisitAPI", "chapter_read.json");
        static bool _fetching;

        public static bool IsRead(string id) => _read.Contains(id);
        public static bool AnyUnread(IEnumerable<string> ids) => ids.Any(id => !_read.Contains(id));
        public static int CountUnread(IEnumerable<string> ids) => ids.Count(id => !_read.Contains(id));

        public static void MarkRead(IEnumerable<string> ids)
        {
            var fresh = ids.Where(_read.Add).ToList();
            if (fresh.Count == 0) return;
            Post(fresh);
        }

        static void Post(List<string> ids) =>
            VisitHttp.Post("/visitapi/quest/read", "{\"ids\":" + JsonConvert.SerializeObject(ids) + "}", "[chapter/read]");

        public static void Sync()
        {
            if (_fetching) return;
            _fetching = true;
            Plugin.Instance.StartCoroutine(VisitHttp.Fetch("/visitapi/quest/read/list", TryParse, "[chapter/read]", ok =>
            {
                if (ok) { Migrate(); return; }
                _fetching = false;
                Plugin.Log.LogWarning("[chapter/read] giving up for now - will retry when tasks screen opens");
            }));
        }

        static bool TryParse(string body)
        {
            try
            {
                if (!(JObject.Parse(body)["data"] is JArray arr)) return false;
                foreach (var t in arr) { var id = t.Value<string>(); if (!string.IsNullOrEmpty(id)) _read.Add(id); }
                Plugin.Log.LogDebug($"[chapter/read] {_read.Count} read id(s) from profile");
                return true;
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("[chapter/read] parse failed: " + e.Message); return false; }
        }

        static void Migrate()
        {
            try
            {
                if (!File.Exists(LegacyPath)) return;
                var old = JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(LegacyPath)) ?? new List<string>();
                MarkRead(old);
                File.Move(LegacyPath, LegacyPath + ".migrated");
                Plugin.Log.LogInfo($"[chapter/read] migrated {old.Count} legacy read id(s) into profile");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("[chapter/read] legacy migrate failed: " + e.Message); }
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
