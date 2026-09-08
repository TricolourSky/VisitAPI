using UnityEngine;

namespace VisitAPI.Native;

/// <summary>RectTransform 小工具：旧版四处各写一遍「铺满父节点」四行（体检第三轮收口）。</summary>
public static class RectExt
{
    /// 锚到四角、边距归零 = 与父节点同大
    public static RectTransform Stretch(this RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }
}
