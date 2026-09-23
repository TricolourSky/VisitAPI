using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace VisitAPI.ChapterUI
{
    public static class TmpFix
    {
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
