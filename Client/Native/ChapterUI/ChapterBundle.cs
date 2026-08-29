using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;

namespace VisitAPI.ChapterUI
{
    /// <summary>
    /// 章节屏的壳：从 plugins/VisitAPI/bundles/visitapi_chapterui.bundle 里把 1.1 的 MainQuestPanel 实例化出来。
    /// bundle 里 TMP 的字体引用是空的（1.1 的字体资产没带），实例化后从任务屏现成的文字上抄字体和材质。DEV_NOTES #69。
    /// </summary>
    public static class ChapterBundle
    {
        static AssetBundle _bundle;

        /// 实例化 bundle 里任一 prefab：清掉 1.1 导出残留的 SubMesh，把字体/材质从任务屏现成文字上抄过来，文本走 TmpFix 重建
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
            var path = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "bundles", "visitapi_chapterui.bundle");
            if (!File.Exists(path)) { Plugin.Log.LogWarning("[chapter] bundle missing: " + path); return null; }
            _bundle = AssetBundle.LoadFromFile(path);
            if (_bundle == null) Plugin.Log.LogWarning("[chapter] bundle failed to load: " + path);
            else Plugin.Log.LogDebug("[chapter] bundle assets: " + string.Join(", ", _bundle.GetAllAssetNames().Take(8)));
            return _bundle;
        }
    }
}
