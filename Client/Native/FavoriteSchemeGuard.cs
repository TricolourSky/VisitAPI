using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace VisitAPI.Native
{
    // EFT persists pinned hideout schemes as a JSON string array in PlayerPrefs ("favorite_scheme")
    // through raw JsonConvert calls. A mod that tampers with Newtonsoft's global DefaultSettings (the
    // DEV_NOTES #1 hazard) breaks BOTH directions: AddFavoriteScheme's SerializeObject throws AFTER
    // List_1.Add (the pin shows all session but is never saved -> rolls back on the next hideout load),
    // and Init's DeserializeObject failure makes the native catch WIPE the stored list to "[]".
    // These patches redo the persistence with hand-written JSON (immune to global settings) and rescue
    // Init's wipe; the TryGetFavoriteIndex finalizer stays as the last-resort crash guard (#10).
    internal static class FavoriteSchemeGuard
    {
        private const BindingFlags All = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string StoreKey = "favorite_scheme";

        private static FieldInfo? _listField;
        private static MethodInfo? _setString;
        private static bool _loggedCrash;
        private static bool _loggedRescue;

        internal static void Apply(Harmony harmony)
        {
            Type? type = AccessTools.TypeByName("PlayerPrefHelperClass");
            if (type == null)
            {
                Plugin.Log.LogWarning("[FavoriteSchemeGuard] PlayerPrefHelperClass not found; guard inactive");
                return;
            }
            _listField = type.GetField("List_1", All);
            _setString = type.GetMethod("SetString", All, null, new[] { typeof(string), typeof(string) }, null);

            MethodInfo? tryGet = type.GetMethod("TryGetFavoriteIndex", All);
            if (tryGet != null)
                harmony.Patch(tryGet, finalizer: new HarmonyMethod(typeof(FavoriteSchemeGuard).GetMethod(nameof(Finalizer), BindingFlags.Static | BindingFlags.NonPublic)));
            else
                Plugin.Log.LogWarning("[FavoriteSchemeGuard] TryGetFavoriteIndex not found; crash guard inactive");

            bool persistence = _listField != null && _setString != null;
            MethodInfo? add = type.GetMethod("AddFavoriteScheme", All);
            MethodInfo? del = type.GetMethod("DeleteFavoriteScheme", All);
            MethodInfo? init = type.GetMethod("Init", All);
            if (persistence && add != null && del != null)
            {
                harmony.Patch(add, prefix: new HarmonyMethod(typeof(FavoriteSchemeGuard).GetMethod(nameof(AddPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
                harmony.Patch(del, prefix: new HarmonyMethod(typeof(FavoriteSchemeGuard).GetMethod(nameof(DeletePrefix), BindingFlags.Static | BindingFlags.NonPublic)));
            }
            if (persistence && init != null)
            {
                harmony.Patch(init,
                    prefix: new HarmonyMethod(typeof(FavoriteSchemeGuard).GetMethod(nameof(InitPrefix), BindingFlags.Static | BindingFlags.NonPublic)),
                    postfix: new HarmonyMethod(typeof(FavoriteSchemeGuard).GetMethod(nameof(InitPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
            }
            Plugin.Log.LogInfo("[FavoriteSchemeGuard] installed (crash guard=" + (tryGet != null)
                + ", settings-immune persistence=" + (persistence && add != null && del != null)
                + ", init rescue=" + (persistence && init != null) + ")");
        }

        private static List<string>? Favorites
        {
            get => _listField?.GetValue(null) as List<string>;
            set => _listField?.SetValue(null, value);
        }

        private static void WriteStore(List<string> ids)
        {
            _setString?.Invoke(null, new object[] { StoreKey, ToJson(ids) });
        }

        // Replaces AddFavoriteScheme: same in-memory effect, hand-written JSON to PlayerPrefs.
        private static bool AddPrefix(string schemeId)
        {
            try
            {
                List<string> list = Favorites ?? new List<string>();
                if (!list.Contains(schemeId)) list.Add(schemeId);
                Favorites = list;
                WriteStore(list);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[FavoriteSchemeGuard] pin save: " + ex.Message); }
            return false;
        }

        // Replaces DeleteFavoriteScheme.
        private static bool DeletePrefix(string schemeId)
        {
            try
            {
                List<string> list = Favorites ?? new List<string>();
                list.Remove(schemeId);
                Favorites = list;
                WriteStore(list);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[FavoriteSchemeGuard] pin remove: " + ex.Message); }
            return false;
        }

        // Capture the stored JSON BEFORE Init runs — the native catch may wipe it to "[]".
        private static void InitPrefix(ref string? __state)
        {
            __state = null;
            try { __state = PlayerPrefs.GetString(StoreKey, "[]"); } catch { }
        }

        // If the native load failed (List_1 null, or wiped empty while the pre-Init store had entries),
        // restore both the in-memory list and the store from the captured JSON. A deliberate profile
        // reset (ResetPrefs) DELETES the key entirely — HasKey false — and must not be resurrected.
        private static void InitPostfix(string? __state)
        {
            try
            {
                if (!PlayerPrefs.HasKey(StoreKey)) return;
                List<string> parsed = ParseStringArray(__state);
                List<string>? live = Favorites;
                if (live != null && (live.Count > 0 || parsed.Count == 0)) return;
                Favorites = parsed;
                WriteStore(parsed);
                if (parsed.Count > 0 && !_loggedRescue)
                {
                    _loggedRescue = true;
                    Plugin.Log.LogWarning("[FavoriteSchemeGuard] native favorite load failed — restored " + parsed.Count + " pinned scheme(s) from PlayerPrefs");
                }
            }
            catch { }
        }

        private static Exception? Finalizer(Exception __exception, ref int index, ref bool __result)
        {
            if (__exception == null) return null;
            if (Favorites == null && _listField != null) Favorites = new List<string>();
            index = -1;
            __result = false;
            if (!_loggedCrash)
            {
                _loggedCrash = true;
                Plugin.Log.LogWarning("[FavoriteSchemeGuard] swallowed " + __exception.GetType().Name + " in TryGetFavoriteIndex; hideout favorites treated as empty");
            }
            return null;
        }

        private static string ToJson(List<string> ids)
        {
            StringBuilder sb = new StringBuilder("[");
            for (int i = 0; i < ids.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(ids[i].Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
            }
            return sb.Append(']').ToString();
        }

        private static List<string> ParseStringArray(string? raw)
        {
            List<string> list = new List<string>();
            if (string.IsNullOrEmpty(raw)) return list;
            bool inString = false;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < raw!.Length; i++)
            {
                char c = raw[i];
                if (!inString)
                {
                    if (c == '"') { inString = true; sb.Length = 0; }
                    continue;
                }
                if (c == '\\' && i + 1 < raw.Length) { sb.Append(raw[++i]); continue; }
                if (c == '"') { inString = false; if (sb.Length > 0) list.Add(sb.ToString()); continue; }
                sb.Append(c);
            }
            return list;
        }
    }
}
