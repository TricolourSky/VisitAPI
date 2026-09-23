using System;
using System.Collections;

namespace VisitAPI.Native;

public static class UiWait
{
    public static IEnumerator Until(Func<bool> done, int maxFrames)
    {
        for (var i = 0; i < maxFrames && !done(); i++) yield return null;
    }
}
