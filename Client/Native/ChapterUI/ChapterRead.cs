using System.Collections.Generic;
using System.IO;
using System.Linq;
using EFT.UI;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.EventSystems;

namespace VisitAPI.ChapterUI
{
    /// <summary>
    /// 1.1 的"未读"小巧思：新日记/新目标旁挂绿色 `!`，鼠标移上去就算读过、标记消失。
    /// 已读 id（日记 noteId / 目标条件 id）持久化在 BepInEx/config/VisitAPI/chapter_read.json；悬停感应用原生 NonDrawingGraphic 接鼠标（不画东西）。
    /// </summary>
    public static class ReadState
    {
        static readonly string StorePath = Path.Combine(BepInEx.Paths.ConfigPath, "VisitAPI", "chapter_read.json");
        static HashSet<string> _read;

        static HashSet<string> Load()
        {
            if (_read != null) return _read;
            try { _read = File.Exists(StorePath) ? new HashSet<string>(JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(StorePath)) ?? new List<string>()) : new HashSet<string>(); }
            catch (System.Exception e) { Plugin.Log.LogWarning("[chapter/read] load failed: " + e.Message); _read = new HashSet<string>(); }
            return _read;
        }

        public static bool IsRead(string id) => Load().Contains(id);
        public static bool AnyUnread(IEnumerable<string> ids) => ids.Any(id => !IsRead(id));

        public static void MarkRead(IEnumerable<string> ids)
        {
            var set = Load(); var changed = false;
            foreach (var id in ids) changed |= set.Add(id);
            if (!changed) return;
            try { Directory.CreateDirectory(Path.GetDirectoryName(StorePath)); File.WriteAllText(StorePath, JsonConvert.SerializeObject(set.ToList())); }
            catch (System.Exception e) { Plugin.Log.LogWarning("[chapter/read] save failed: " + e.Message); }
        }

        /// 给一块区域挂鼠标进入回调（用 prefab 自带的 HoverReadTrigger，没有就补一个）：区域自身若没有可接收射线的 Graphic，补一个 NonDrawingGraphic
        public static void OnHover(GameObject area, System.Action onEnter)
        {
            if (area == null) return;
            if (area.GetComponent<UnityEngine.UI.Graphic>() == null) area.AddComponent<NonDrawingGraphic>().raycastTarget = true;
            var h = area.GetComponent<HoverReadTrigger>() ?? area.AddComponent<HoverReadTrigger>(); h.Enter = onEnter;
        }
    }
}
