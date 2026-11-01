using System.IO;
using TMPro;
using UnityEngine;

namespace VisitAPI.ChapterUI
{
    /// <summary>框架自己的一个界面包（ui\*.bundle，老的 bundles\ 还认，见 VisitPaths）：按文件各加载一次。
    /// 包是在隔离 SDK 里打的，文字带的 TMP_SubMeshUI 是打包机器上的字体生成的，到这边是错的——实例化后一律删掉，再把字体换成游戏里现成的。
    /// 章节界面包（ChapterBundle）和访问加载屏（NarrateLoading）共用（10-24 收拢，旧 D11）；三条日志的措辞各家不同，建的时候各传各的（{0} = 路径 / 预制体名）。</summary>
    public sealed class UiBundle
    {
        readonly string _file, _missing, _failed, _noPrefab;
        AssetBundle _bundle;

        public UiBundle(string file, string missing, string failed, string noPrefab)
        {
            _file = file; _missing = missing; _failed = failed; _noPrefab = noPrefab;
        }

        public GameObject Prefab(string name) => Load()?.LoadAsset<GameObject>(name);
        public AudioClip Clip(string name) => Load()?.LoadAsset<AudioClip>(name);

        /// <summary>实例化到 host 下并换字体，换完给每个文字重排一次（TmpFix.Set）。</summary>
        public GameObject Instantiate(string prefabName, Transform host, TMP_Text fontTemplate)
        {
            var prefab = Prefab(prefabName);
            if (prefab == null) { Plugin.Log.LogWarning(string.Format(_noPrefab, prefabName)); return null; }
            var go = Object.Instantiate(prefab, host, false);
            CopyFont(go, fontTemplate, refit: true);
            return go;
        }

        /// <summary>包里带的 TMP_SubMeshUI 一律删掉；fontTemplate 给了就把 go 下全部文字换成它的字体和材质。refit = 换完字体给每个文字重排一次（TmpFix.Set，章节页要；加载屏的名字是显示时才 Set 的，不用）。</summary>
        public static void CopyFont(GameObject go, TMP_Text fontTemplate, bool refit)
        {
            foreach (var sub in go.GetComponentsInChildren<TMP_SubMeshUI>(true)) Object.DestroyImmediate(sub.gameObject);
            if (fontTemplate == null) return;
            foreach (var t in go.GetComponentsInChildren<TMP_Text>(true))
            {
                t.font = fontTemplate.font;
                t.fontSharedMaterial = fontTemplate.fontSharedMaterial;
                if (refit) TmpFix.Set(t, t.text);
            }
        }

        /// <summary>root 下第一个带字体的现成文字，拿来当字体模板（exclude 下面的不算——别把我们自己刚实例化的那份当模板）；没有就 null。</summary>
        public static TMP_Text FirstFont(Transform root, Transform exclude = null)
        {
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
                if (t != null && t.font != null && (exclude == null || !t.transform.IsChildOf(exclude))) return t;
            return null;
        }

        AssetBundle Load()
        {
            if (_bundle != null) return _bundle;
            var path = Path.Combine(VisitPaths.Ui, _file);
            if (!File.Exists(path)) { Plugin.Log.LogWarning(string.Format(_missing, path)); return null; }
            _bundle = AssetBundle.LoadFromFile(path);
            if (_bundle == null) Plugin.Log.LogWarning(string.Format(_failed, path));
            return _bundle;
        }
    }

    /// 章节界面包 visitapi_chapterui.bundle（章节页、横幅、链接物品行、音效）
    public static class ChapterBundle
    {
        static readonly UiBundle Bundle = new("visitapi_chapterui.bundle", "[chapter] bundle missing: {0}", "[chapter] bundle failed to load: {0}", "[chapter] {0} not found in bundle");

        public static GameObject Instantiate(string prefabName, Transform host, TextMeshProUGUI fontTemplate) => Bundle.Instantiate(prefabName, host, fontTemplate);

        public static GameObject Prefab(string name) => Bundle.Prefab(name);
        public static AudioClip Clip(string name) => Bundle.Clip(name);
    }
}
