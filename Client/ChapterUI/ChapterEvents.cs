using UnityEngine;

namespace VisitAPI.Native;

/// <summary>G7：章节屏的"该重画了"信号灯。任务状态变化/新任务进书/flags 到货时点亮，
/// ChapterLive 在自己的 Update 里消费（一帧最多重画一次，天然合并连环事件）。取代旧版 0.5s 轮询快照。</summary>
public static class ChapterEvents
{
    static int _stamp;

    public static void Raise() => _stamp++;

    /// 消费式检查：自上次调用后有没有新信号（lastSeen 由调用方持有）
    public static bool Changed(ref int lastSeen)
    {
        if (lastSeen == _stamp) return false;
        lastSeen = _stamp;
        return true;
    }

    /// 供 ChapterLive 初始化基线
    public static int Stamp => _stamp;
}
