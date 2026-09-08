using EFT.Quests;
using UnityEngine;
using VisitAPI.Native;

namespace VisitAPI.ChapterUI
{
    /// <summary>G7：章节屏开着时的刷新引擎，取代旧版"0.5s 轮询字符串快照 → 整屏 Destroy 重建"。
    /// ① 任务状态变化/新任务进书/flags 到货 → ChapterEvents 点灯 → 下一帧整屏重画一次（保住选中章节，连环事件天然合并）；
    /// ② 计数类目标的进度（击杀 3/5 → 4/5）不走任务状态事件 → 每 0.5s 只对屏上的行做**原位**样式刷新，快照没变的行一个字都不动。</summary>
    public class ChapterLive : MonoBehaviour
    {
        MainQuestTabView _view; QuestController _quests; int _stamp; float _next;

        public static void Attach(MainQuestTabView view, QuestController quests)
        {
            var live = view.GetComponent<ChapterLive>();
            if (live == null) live = view.gameObject.AddComponent<ChapterLive>();
            live._view = view; live._quests = quests;
            live._stamp = ChapterEvents.Stamp;   // Show 刚画完的就是最新状态，旧信号一笔勾销
        }

        void Update()
        {
            if (_view == null || _quests == null) return;
            if (ChapterEvents.Changed(ref _stamp)) { _view.Show(_quests, keepSelection: true); return; }
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;
            foreach (var t in _view.LiveRows)
            {
                if (t.Row == null || !t.Row.gameObject.activeSelf) continue;
                // 轻量快照（状态 + 计数值）与 Style 同一个出口（ChapterStates.Snap），变了才让 Style 重排版
                if (ChapterStates.Snap(t.Quest, t.Cond, t.Over).Key == t.Last) continue;
                t.Last = _view.Style(t.Row, t.Quest, t.Cond, t.Over, t.Indent);
            }
        }
    }
}
