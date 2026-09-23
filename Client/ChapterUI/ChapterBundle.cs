using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;

namespace VisitAPI.ChapterUI
{
    public static class ChapterBundle
    {
        static AssetBundle _bundle;

        public static GameObject Instantiate(string prefabName, Transform host, TextMeshProUGUI fontTemplate)
        {
            var prefab = Load()?.LoadAsset<GameObject>(prefabName);
            if (prefab == null) { Plugin.Log.LogWarning($"[chapter] {prefabName} not found in bundle"); return null; }
            var go = Object.Instantiate(prefab, host, false);
            foreach (var sub in go.GetComponentsInChildren<TMP_SubMeshUI>(true)) Object.DestroyImmediate(sub.gameObject);
            if (fontTemplate != null)
                foreach (var t in go.GetComponentsInChildren<TMP_Text>(true))
                {
                    t.font = fontTemplate.font;
                    t.fontSharedMaterial = fontTemplate.fontSharedMaterial;
                    TmpFix.Set(t, t.text);
                }
            return go;
        }

        public static GameObject Prefab(string name) => Load()?.LoadAsset<GameObject>(name);
        public static AudioClip Clip(string name) => Load()?.LoadAsset<AudioClip>(name);

        static AssetBundle Load()
        {
            if (_bundle != null) return _bundle;
            var path = Path.Combine(VisitPaths.Ui, "visitapi_chapterui.bundle");   // ui\（老的 bundles\ 还认，见 VisitPaths）
            if (!File.Exists(path)) { Plugin.Log.LogWarning("[chapter] bundle missing: " + path); return null; }
            _bundle = AssetBundle.LoadFromFile(path);
            if (_bundle == null) Plugin.Log.LogWarning("[chapter] bundle failed to load: " + path);
            else Plugin.Log.LogDebug("[chapter] bundle assets: " + string.Join(", ", _bundle.GetAllAssetNames().Take(8)));
            return _bundle;
        }
    }
}
