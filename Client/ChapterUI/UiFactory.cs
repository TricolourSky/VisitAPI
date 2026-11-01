using UnityEngine;
using UnityEngine.UI;

namespace VisitAPI.ChapterUI
{
    /// <summary>章节页上自己建的小图片物体（ChapterTab / ChapterLinks 共用；10-24 收拢，旧 B17）。</summary>
    public static class UiFactory
    {
        /// 一个带 RectTransform + Image 的子物体，不挡点击；ignoreLayout = 不进父物体的布局组。锚点 / 颜色由调用方自己定
        public static Image NewImage(Transform parent, string name, bool ignoreLayout = false)
        {
            var go = ignoreLayout
                ? new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement))
                : new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            if (ignoreLayout) go.GetComponent<LayoutElement>().ignoreLayout = true;
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            return img;
        }
    }
}
