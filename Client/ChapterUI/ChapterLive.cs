using EFT.Quests;
using UnityEngine;
using VisitAPI.Native;

namespace VisitAPI.ChapterUI
{
    public class ChapterLive : MonoBehaviour
    {
        MainQuestTabView _view; QuestController _quests; int _stamp; float _next;

        public static void Attach(MainQuestTabView view, QuestController quests)
        {
            var live = view.GetComponent<ChapterLive>();
            if (live == null) live = view.gameObject.AddComponent<ChapterLive>();
            live._view = view; live._quests = quests;
            live._stamp = ChapterEvents.Stamp;
        }

        void Update()
        {
            if (_view == null || _quests == null) return;
            if (ChapterEvents.Changed(ref _stamp)) { _view.Show(_quests, keepSelection: true); return; }
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;
            if (_view.RowsStale()) { Plugin.Log.LogInfo("[chapter] 可见目标数变了（目标达成后新目标开门），整屏重画"); _view.Show(_quests, keepSelection: true); return; }
            foreach (var t in _view.LiveRows)
            {
                if (t.Row == null || !t.Row.gameObject.activeSelf) continue;
                if (ChapterStates.Snap(t.Quest, t.Cond, t.Over).Key == t.Last) continue;
                t.Last = _view.Style(t.Row, t.Quest, t.Cond, t.Over);
            }
        }
    }
}
