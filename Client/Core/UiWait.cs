using System;
using System.Collections;

namespace VisitAPI.Native;

/// <summary>协程等待工具：旧版九处各写一套 `for + yield return null`，收口到这里。</summary>
public static class UiWait
{
    /// <summary>逐帧等 <paramref name="done"/> 为真，最多 <paramref name="maxFrames"/> 帧。到底了没等到也返回（调用方自己复查条件）。</summary>
    public static IEnumerator Until(Func<bool> done, int maxFrames)
    {
        for (var i = 0; i < maxFrames && !done(); i++) yield return null;
    }
}
