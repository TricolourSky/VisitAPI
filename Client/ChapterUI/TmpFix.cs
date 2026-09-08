using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace VisitAPI.ChapterUI
{
    /// <summary>1.1 导出的 prefab 里 TMP 组件带着当年截场景时的 textInfo（十几条 meshInfo，顶点里还是旧文字），
    /// 运行时 TMP 会照着它补建一堆 SubMesh 子物件，把旧顶点画出来（红方块 + 俄文幽灵，DEV_NOTES #70）。
    /// 统一走这里赋值：强制重建网格，再把 TMP 自己表里没有的孤儿 SubMesh 销毁。</summary>
    public static class TmpFix
    {
        // m_subTextObjects 声明在 TextMeshProUGUI（子类）上不在 TMP_Text 上——AccessTools 只沿基类向上找，
        // 从基类查会扑空、孤儿判定拿到空表把 TMP 自己管的 SubMesh 也当孤儿杀。按实例真实类型查、按类型缓存。
        static readonly Dictionary<System.Type, FieldInfo> _subFields = new();

        public static void Set(TMP_Text t, string text)
        {
            if (t == null) return;
            t.text = text;
            t.ForceMeshUpdate(true, true);
            var subs = t.GetComponentsInChildren<TMP_SubMeshUI>(true); if (subs.Length == 0) return;
            var type = t.GetType();
            if (!_subFields.TryGetValue(type, out var f)) _subFields[type] = f = AccessTools.Field(type, "m_subTextObjects");
            var owned = f?.GetValue(t) as TMP_SubMeshUI[] ?? new TMP_SubMeshUI[0];
            foreach (var sub in subs) if (!owned.Contains(sub)) Object.Destroy(sub.gameObject);
        }
    }
}
