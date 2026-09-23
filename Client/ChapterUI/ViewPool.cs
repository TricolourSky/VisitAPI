using System.Collections.Generic;
using UnityEngine;

namespace VisitAPI.ChapterUI
{
    public class ViewPool : MonoBehaviour
    {
        readonly List<GameObject> _items = new();
        GameObject _template;
        int _used;

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
            go.transform.SetAsLastSibling();
            return go;
        }

        public void ReleaseAll()
        {
            foreach (var go in _items) if (go != null) go.SetActive(false);
            _used = 0;
        }
    }
}
