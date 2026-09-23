using UnityEngine;

namespace VisitAPI.Native;

public static class ChapterEvents
{
    static int _stamp;

    public static void Raise() => _stamp++;

    public static bool Changed(ref int lastSeen)
    {
        if (lastSeen == _stamp) return false;
        lastSeen = _stamp;
        return true;
    }

    public static int Stamp => _stamp;
}
