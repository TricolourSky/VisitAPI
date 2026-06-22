using System.Collections.Generic;

namespace VisitAPI
{
    // Runtime quest-status helper for option visibility gating (and, later, handover auto-show + the trader
    // "visit" tab). Reads the LIVE status from the native quest book each call — the player can change quest
    // state mid-dialog (accept/setstatus), so we never cache a stale value across a render.
    internal static class QuestStatusCache
    {
        internal static int? StatusOf(string questId) => NativeQuestController.GetQuestStatus(questId);

        // True if the current status matches any of the named statuses (names or 0–5). A null current
        // status (quest not in the book) matches nothing.
        internal static bool InAny(int? current, List<string> statusNames)
        {
            if (current == null || statusNames == null) return false;
            foreach (string n in statusNames)
            {
                int? s = Parse(n);
                if (s.HasValue && s.Value == current.Value) return true;
            }
            return false;
        }

        internal static int? Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            s = s.Trim();
            if (int.TryParse(s, out int n) && n >= 0 && n <= 5) return n;
            switch (s.ToLowerInvariant())
            {
                case "locked": return 0;
                case "availableforstart": return 1;
                case "started": return 2;
                case "availableforfinish": return 3;
                case "success": return 4;
                case "fail": return 5;
                default: return null;
            }
        }
    }
}
