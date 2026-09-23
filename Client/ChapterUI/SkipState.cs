using System.Collections.Generic;
using System.IO;
using System.Linq;
using EFT.Quests;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VisitAPI.Native;

namespace VisitAPI.ChapterUI
{
    public static class SkipState
    {
        class Data { public HashSet<string> Done = new(); public HashSet<string> Skipped = new(); public Dictionary<string, int> Counts = new(); }
        static string _profile;
        static Data _data = new();
        static string Dir => Path.Combine(BepInEx.Paths.ConfigPath, "VisitAPI");
        static string LegacyPath(string profile) => Path.Combine(Dir, "objectives." + profile + ".json");

        public static void Use(string profileId)
        {
            if (string.IsNullOrEmpty(profileId) || profileId == _profile) return;
            _profile = profileId; _data = new Data();
            Plugin.Instance.StartCoroutine(VisitHttp.Fetch("/visitapi/quest/objectives/list", TryParse, "[objectives]", ok =>
            {
                if (ok) MigrateLegacy(profileId);
                else Plugin.Log.LogWarning("[objectives] giving up for now - records stay local for this session");
            }));
        }

        static bool TryParse(string body)
        {
            try
            {
                if (!(JObject.Parse(body)["data"] is JObject data)) return false;
                Merge(_data, data);
                Plugin.Log.LogDebug($"[objectives] {_data.Done.Count} done / {_data.Skipped.Count} skipped from profile");
                return true;
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("[objectives] parse failed: " + e.Message); return false; }
        }

        static void Merge(Data into, JObject src)
        {
            if (src["done"] is JArray d) foreach (var t in d) { var s = t.Value<string>(); if (!string.IsNullOrEmpty(s)) { into.Done.Add(s); into.Skipped.Remove(s); } }
            if (src["skipped"] is JArray k) foreach (var t in k) { var s = t.Value<string>(); if (!string.IsNullOrEmpty(s) && !into.Done.Contains(s)) into.Skipped.Add(s); }
            if (src["counts"] is JObject c) foreach (var p in c.Properties()) if (p.Value.Type == JTokenType.Integer) into.Counts[p.Name] = p.Value.Value<int>();
        }

        static void MigrateLegacy(string profileId)
        {
            try
            {
                var path = LegacyPath(profileId);
                if (!File.Exists(path)) return;
                var old = JsonConvert.DeserializeObject<Data>(File.ReadAllText(path));
                if (old != null)
                {
                    var delta = new JObject { ["done"] = new JArray(old.Done), ["skipped"] = new JArray(old.Skipped), ["counts"] = JObject.FromObject(old.Counts) };
                    Merge(_data, delta);
                    Post(delta);
                }
                File.Move(path, path + ".migrated");
                Plugin.Log.LogInfo($"[objectives] 旧本地记录并入档案：{old?.Done.Count ?? 0} done / {old?.Skipped.Count ?? 0} skipped");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("[objectives] legacy migrate failed: " + e.Message); }
        }

        public static bool IsSkipped(string condId) => _data.Skipped.Contains(condId) && !_data.Done.Contains(condId);

        public static int Count(string condId) => _data.Counts.TryGetValue(condId, out var n) ? n : 0;

        public static void Note(Quest quest)
        {
            if (_profile == null || quest?.Template?.Conditions == null) return;
            var st = quest.QuestStatus;
            if (st != EQuestStatus.Started && st != EQuestStatus.AvailableForFinish) return;
            if (!quest.Template.Conditions.TryGetValue(EQuestStatus.AvailableForFinish, out var list) || list == null) return;
            var done = new List<string>(); var skipped = new List<string>(); var counts = new Dictionary<string, int>();
            try
            {
                foreach (var c in list)
                {
                    if (c == null || !c.ParentId.HasValue) continue;
                    var parent = list.FirstOrDefault(p => p != null && p.id == c.ParentId.Value);
                    if (parent == null) continue;
                    var id = c.id.ToString();
                    if (ChapterStates.DoneRaw(quest, c)) { if (_data.Done.Add(id)) done.Add(id); _data.Skipped.Remove(id); }
                    else if (ChapterStates.DoneRaw(quest, parent))
                    {
                        if (_data.Skipped.Add(id)) skipped.Add(id);
                        if (quest.ProgressCheckers.TryGetValue(c, out var pc) && pc != null && pc.HasGetter())
                        {
                            var cur = (int)pc.CurrentValue;
                            if (!_data.Counts.TryGetValue(id, out var old) || old != cur) { _data.Counts[id] = cur; counts[id] = cur; }
                        }
                    }
                }
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("[objectives] 记录子目标结果失败: " + e.Message); }
            if (done.Count == 0 && skipped.Count == 0 && counts.Count == 0) return;
            Post(new JObject { ["done"] = new JArray(done), ["skipped"] = new JArray(skipped), ["counts"] = JObject.FromObject(counts) });
        }

        static void Post(JObject delta) => VisitHttp.Post("/visitapi/quest/objectives", delta.ToString(Formatting.None), "[objectives]");
    }
}
