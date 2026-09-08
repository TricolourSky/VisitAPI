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
    /// <summary>1.1 的"未读"巧思：新日记/新目标/新物品旁挂绿 `!`，鼠标移上去（或开着章节 4 秒）就算读过。
    /// G4/B1：已读 id 存**服务端档案**（/visitapi/quest/read，跟档案走，换档不串）；本地只留会话内缓存。
    /// B2：只增量上报新 id、异步打完就走，不再悬停一次全量写盘。旧的全局 chapter_read.json 首次同步时并入档案后改名封存。</summary>
    public static class ReadState
    {
        static readonly HashSet<string> _read = new();
        static readonly string LegacyPath = Path.Combine(BepInEx.Paths.ConfigPath, "VisitAPI", "chapter_read.json");
        static bool _fetching;
        /// 已读集合的版本号：每次变化 +1。屏外徽章（UnreadBadge）靠比它省掉无谓重算
        public static int Version;

        public static bool IsRead(string id) => _read.Contains(id);
        public static bool AnyUnread(IEnumerable<string> ids) => ids.Any(id => !_read.Contains(id));
        public static int CountUnread(IEnumerable<string> ids) => ids.Count(id => !_read.Contains(id));   // G2：未读徽章带计数

        public static void MarkRead(IEnumerable<string> ids)
        {
            var fresh = ids.Where(_read.Add).ToList();
            if (fresh.Count == 0) return;
            Version++;
            Post(fresh);
        }

        static void Post(List<string> ids) =>
            VisitHttp.Post("/visitapi/quest/read", "{\"ids\":" + JsonConvert.SerializeObject(ids) + "}", "[chapter/read]");

        /// 登录后拉一次档案里的已读表（QuestFlags 拿到 flags 后调）；顺带做旧文件一次性迁移。
        /// 12 次全败别锁死本会话（阶段四审计）：放弃时复位 _fetching，下次开任务屏 Sync() 再试
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
                Version++;
                Plugin.Log.LogDebug($"[chapter/read] {_read.Count} read id(s) from profile");
                return true;
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("[chapter/read] parse failed: " + e.Message); return false; }
        }

        /// 1.2.1 的全局已读文件并进档案（只跑一次，跑完改名 .migrated 封存；失败下次启动再试）
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

        /// 给一块区域挂鼠标进入回调（用 prefab 自带的 HoverReadTrigger，没有就补一个）；区域自身没有可接收射线的 Graphic 时补一个 NonDrawingGraphic
        public static void OnHover(GameObject area, System.Action onEnter)
        {
            if (area == null) return;
            if (area.GetComponent<UnityEngine.UI.Graphic>() == null) area.AddComponent<NonDrawingGraphic>().raycastTarget = true;
            var h = area.GetComponent<HoverReadTrigger>() ?? area.AddComponent<HoverReadTrigger>();
            h.Enter = onEnter;
        }
    }
}
