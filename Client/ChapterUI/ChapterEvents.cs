namespace VisitAPI.Native;

public static class ChapterEvents
{
    public static int Stamp { get; private set; }

    public static void Raise() => Stamp++;

    public static bool Changed(ref int lastSeen)
    {
        if (lastSeen == Stamp) return false;
        lastSeen = Stamp;
        return true;
    }
}
