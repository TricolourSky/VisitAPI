using UnityEngine;

namespace VisitAPI.Native;

public static class RectExt
{
    public static RectTransform Stretch(this RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }
}
