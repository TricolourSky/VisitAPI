using System.Collections.Generic;
using EFT.Quests;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VisitAPI.Native;

namespace VisitAPI.ChapterUI
{
    public static class SkipState
    {
        class Data { public HashSet<string> Done = new(); public HashSet<string> Skipped = new(); public Dictionary<string, int> Counts = new(); }
        static string _profile;
        static Data _data = new();
        static readonly Dictionary<string, int> _notedAt = new();   // 任务 id → 上次 Note 的帧

        public static void Use(string profileId)
        {
            if (string.IsNullOrEmpty(profileId) || profileId == _profile) return;
            _profile = profileId; _data = new Data();
            Plugin.Instance.StartCoroutine(VisitHttp.Fetch("/visitapi/quest/objectives/list", TryParse, "[objectives]", ok =>
            {
                if (!ok) Plugin.Log.LogWarning("[objectives] giving up for now - records stay local for this session");
            }));
        }

        static bool TryParse(string body)
        {
            try
            {
                if (JObject.Parse(body)["data"] is not JObject data) return false;
                Merge(_data, data);
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

        public static bool IsSkipped(string condId) => _data.Skipped.Contains(condId) && !_data.Done.Contains(condId);

        public static int Count(string condId) => _data.Counts.TryGetValue(condId, out var n) ? n : 0;

        /// 记下这条任务里「父目标完成后」各子目标的结果（做完 / 跳过 / 跳过时的计数），变了才发服务端。
        /// 10-24（旧 B24）：章节页每 0.5 秒给每一行都调一次，同一帧里一个任务只算一遍
        public static void Note(Quest quest)
        {
            if (_profile == null || quest?.Template?.Conditions == null) return;
            if (!ChapterStates.Running(quest.QuestStatus)) return;
            var frame = Time.frameCount;
            if (_notedAt.TryGetValue(quest.Id, out var at) && at == frame) return;
            _notedAt[quest.Id] = frame;
            var done = new List<string>(); var skipped = new List<string>(); var counts = new Dictionary<string, int>();
            try
            {
                foreach (var c in quest.Conds(EQuestStatus.AvailableForFinish))
                {
                    if (c == null || !c.ParentId.HasValue) continue;
                    var parent = ChapterStates.Parent(quest, c);
                    if (parent == null) continue;
                    var id = c.id.ToString();
                    var parentDone = ChapterStates.DoneRaw(quest, parent);
                    // 10-15：写了 doneWithParent 的可选子目标，父目标完成就按完成记（见 ChapterStates.Row）
                    if (ChapterStates.DoneRaw(quest, c) || (parentDone && QuestFlags.DoneWithParent(quest.Id, id))) { if (_data.Done.Add(id)) done.Add(id); _data.Skipped.Remove(id); }
                    else if (parentDone)
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
            catch (System.Exception e) { Plugin.Log.LogWarning("[objectives] Failed to record sub-objective results: " + e.Message); }
            if (done.Count == 0 && skipped.Count == 0 && counts.Count == 0) return;
            Post(new JObject { ["done"] = new JArray(done), ["skipped"] = new JArray(skipped), ["counts"] = JObject.FromObject(counts) });
        }

        static void Post(JObject delta) => VisitHttp.Post("/visitapi/quest/objectives", delta.ToString(Formatting.None), "[objectives]");
    }
}
