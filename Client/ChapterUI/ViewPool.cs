using System.Collections.Generic;
using UnityEngine;

namespace VisitAPI.ChapterUI
{
    /// <summary>G8/B5：模板克隆件的池子（1.1 UiElementPool 的我们版）。挂在容器上，Acquire 复用已建实例、不够才 Instantiate，
    /// ReleaseAll 只隐藏不销毁 —— 旧版整屏 Destroy(帧末生效)+立刻 Instantiate 造成同帧新旧并存布局跳动，从根上消掉。</summary>
    public class ViewPool : MonoBehaviour
    {
        readonly List<GameObject> _items = new();
        GameObject _template;
        int _used;

        /// 每个 (容器, 模板) 一个池；容器上挂多个池时按模板区分
        public static ViewPool For(Transform container, GameObject template)
        {
            foreach (var p in container.GetComponents<ViewPool>()) if (p._template == template) return p;
            var pool = container.gameObject.AddComponent<ViewPool>();
            pool._template = template;
            return pool;
        }

        public GameObject Acquire()
        {
            GameObject go;
            if (_used < _items.Count) go = _items[_used];
            else { go = Object.Instantiate(_template, transform, false); _items.Add(go); }
            _used++;
            go.SetActive(true);
            go.transform.SetAsLastSibling();   // 按取用次序排到队尾 → 最终顺序 = 调用顺序（模板在哪个槽位都不影响）
            return go;
        }

        public void ReleaseAll()
        {
            foreach (var go in _items) if (go != null) go.SetActive(false);
            _used = 0;
        }
    }
}
