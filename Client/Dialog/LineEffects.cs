using System.Collections.Generic;
using EFT;
using EFT.UI;

namespace VisitAPI.Native;

public sealed class LineEffect
{
    public (string trader, double delta)? Standing;
    public (string quest, int status)? SetStatus;
    public (MongoID id, int value)? SyncVar;
    public string HandoverQuest;
    public TraderScreensGroup.ETraderMode? Tab;
    public (string trader, string node, int option)? Once;
}

public static class LineEffects
{
    static readonly Dictionary<MongoID, LineEffect> _byLine = new();

    public static LineEffect For(MongoID lineId)
    {
        if (!_byLine.TryGetValue(lineId, out var e)) _byLine[lineId] = e = new LineEffect();
        return e;
    }

    public static bool TryGet(MongoID lineId, out LineEffect e) => _byLine.TryGetValue(lineId, out e);
}
