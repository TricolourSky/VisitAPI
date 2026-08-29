using System.Linq;
using EFT.Quests;
using UnityEngine;

namespace VisitAPI.ChapterUI
{
    /// <summary>章节屏开着的时候盯着任务状态：从对话/战局回来、或者进度变了，半秒内自动重画（保留当前选中的章节和日记展开态）。
    /// 只看屏上已经建好的章节模型（不重扫整本任务书）；任务书变大（服务端发来新子任务）也算变化。DEV_NOTES #71。</summary>
    public class ChapterLive : MonoBehaviour
    {
        MainQuestTabView _view; QuestController _quests; float _next; string _last;

        public static void Attach(MainQuestTabView view, QuestController quests)
        {
            var live = view.GetComponent<ChapterLive>() ?? view.gameObject.AddComponent<ChapterLive>();
            live._view = view; live._quests = quests; live._last = live.Snapshot();
        }

        void Update()
        {
            if (_view == null || _quests == null || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;
            var now = Snapshot(); if (now == _last) return;
            _last = now; _view.Show(_quests, keepSelection: true);
        }

        string Snapshot()
        {
            var sb = new System.Text.StringBuilder(); var book = _quests.Quests; if (book == null) return "";
            sb.Append(book.Count()).Append('|');
            foreach (var q in _view.Chapters.SelectMany(ch => new[] { ch.Quest }.Concat(ch.Subs)))
            {
                sb.Append((int)q.QuestStatus).Append(',');
                if (q.QuestStatus >= EQuestStatus.Started && q.Template.Conditions.TryGetValue(EQuestStatus.AvailableForFinish, out var cc))
                    foreach (var c in cc) sb.Append(q.IsConditionDone(c) ? "d" : q.ProgressCheckers.TryGetValue(c, out var pc) && pc.HasGetter() ? ((int)pc.CurrentValue).ToString() : "0").Append(',');
            }
            return sb.ToString();
        }
    }
}
