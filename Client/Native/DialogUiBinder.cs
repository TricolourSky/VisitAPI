using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace VisitAPI.Native
{
    internal static class DialogUiBinder
    {
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static Type? _windowType;
        internal static Type? RowType { get; private set; }

        private static FieldInfo? _screenWindow;
        private static FieldInfo? _winTraderText;
        private static FieldInfo? _winDialogRow;
        private static FieldInfo? _winLinesContainer;
        private static FieldInfo? _winOptionsCanvasGroup;
        private static FieldInfo? _rowText;
        private static MethodInfo? _screenClose;
        private static MethodInfo? _rowHighlight;
        private static FieldInfo? _screenSubtitles;   // TraderDialogScreen._subtitlesView (the narration box)
        private static FieldInfo? _subtitleTextField; // SubtitlesView._textField
        private static Type? _tmpTextType;
        private static PropertyInfo? _tmpTextProp;
        private static MethodInfo? _tmpForceMeshUpdate;

        private static readonly Dictionary<string, Texture2D> _bgCache = new Dictionary<string, Texture2D>();

        // Current looping video background (mp4) + its render target, when the active node uses a video bg.
        private static VideoPlayer? _bgVideo;
        private static RenderTexture? _bgRT;

        internal static bool Ready { get; private set; }

        internal static void Bind()
        {
            _windowType = AccessTools.TypeByName("EFT.UI.TraderDialogWindow");
            RowType = AccessTools.TypeByName("EFT.UI.TraderDialogWindowOptionRow");

            _screenWindow = NativeBinder.TraderDialogScreenType?.GetField("_dialogWindow", All);
            _winTraderText = _windowType?.GetField("_traderText", All);
            _winDialogRow = _windowType?.GetField("_dialogRow", All);
            _winLinesContainer = _windowType?.GetField("_linesContainer", All);
            _winOptionsCanvasGroup = _windowType?.GetField("_optionsCanvasGroup", All);
            _rowText = RowType?.GetField("_text", All);
            _screenClose = FindMethodUp(NativeBinder.TraderDialogScreenType, "Close");
            _rowHighlight = RowType?.GetMethod("method_1", All, null, new[] { typeof(bool) }, null);
            _screenSubtitles = NativeBinder.TraderDialogScreenType?.GetField("_subtitlesView", All);
            _subtitleTextField = _screenSubtitles?.FieldType.GetField("_textField", All);

            _tmpTextType = AccessTools.TypeByName("TMPro.TMP_Text");
            _tmpTextProp = _tmpTextType?.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
            if (_tmpTextType != null)
                foreach (MethodInfo m in _tmpTextType.GetMethods(BindingFlags.Instance | BindingFlags.Public))
                    if (m.Name == "ForceMeshUpdate") { _tmpForceMeshUpdate = m; break; }

            Ready = _windowType != null && RowType != null && _screenWindow != null
                && _winTraderText != null && _winDialogRow != null && _winLinesContainer != null && _rowText != null;

            void Line(string n, bool ok) => Plugin.Log.LogInfo($"[DialogUiBinder] {(ok ? "PASS" : "FAIL")}  {n}");
            Line("TraderDialogWindow", _windowType != null);
            Line("TraderDialogWindowOptionRow", RowType != null);
            Line("TraderDialogScreen._dialogWindow", _screenWindow != null);
            Line("Window._traderText", _winTraderText != null);
            Line("Window._dialogRow", _winDialogRow != null);
            Line("Window._linesContainer", _winLinesContainer != null);
            Line("Window._optionsCanvasGroup", _winOptionsCanvasGroup != null);
            Line("OptionRow._text", _rowText != null);
            Line("TraderDialogScreen.Close()", _screenClose != null);
            Line("OptionRow.method_1(bool)", _rowHighlight != null);
            Line("TMP_Text.ForceMeshUpdate (close NRE guard)", _tmpForceMeshUpdate != null);
            Line("TraderDialogScreen._subtitlesView (narration box)", _screenSubtitles != null);
            Line("SubtitlesView._textField", _subtitleTextField != null);
            Plugin.Log.LogInfo($"[DialogUiBinder] Ready = {Ready}");
        }

        internal static object? FindActiveScreen()
        {
            Type? t = NativeBinder.TraderDialogScreenType;
            if (t == null || _screenWindow == null) return null;
            foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(t))
            {
                if (!(o is Component comp)) continue;
                if (!comp.gameObject.activeInHierarchy) continue;
                if (_screenWindow.GetValue(o) != null) return o;
            }
            return null;
        }

        internal static object? GetWindow(object screen) => _screenWindow?.GetValue(screen);

        internal static void ShowWindow(object window)
        {
            if (window is Component comp) comp.gameObject.SetActive(true);
        }

        internal static void SetTraderText(object window, string text)
        {
            SetTmpText(_winTraderText?.GetValue(window), text);
        }

        // ---- Narration subtitle box (TraderDialogScreen._subtitlesView) — narration's OWN dialog box ----
        internal static object? GetSubtitlesView(object? screen) => screen != null ? _screenSubtitles?.GetValue(screen) : null;

        // Enter narration: show the native subtitle view, hide the main DialogContainer (cinematic, full-screen).
        internal static void BeginNarration(object? screen)
        {
            if (!(screen is Component sc)) return;
            if (GetSubtitlesView(screen) is Component sv) sv.gameObject.SetActive(true);
            Transform dc = sc.transform.Find("DialogContainer");
            if (dc != null) dc.gameObject.SetActive(false);
        }

        // Leave narration: hide the subtitle view, restore the main DialogContainer.
        internal static void EndNarration(object? screen)
        {
            if (!(screen is Component sc)) return;
            if (GetSubtitlesView(screen) is Component sv) sv.gameObject.SetActive(false);
            Transform dc = sc.transform.Find("DialogContainer");
            if (dc != null) dc.gameObject.SetActive(true);
        }

        internal static void SetSubtitleText(object? screen, string text)
        {
            object? sv = GetSubtitlesView(screen);
            if (sv == null) return;
            if (sv is Component svc) svc.gameObject.SetActive(true);
            object? tf = _subtitleTextField?.GetValue(sv);
            if (tf != null) { SetTmpText(tf, text); return; }
            // Fallback (field renamed): first TMP/Text child under the subtitle view.
            if (sv is Component c && _tmpTextType != null)
                foreach (Component t in c.GetComponentsInChildren(_tmpTextType, true)) { SetTmpText(t, text); return; }
        }

        internal static UnityEngine.Object? InstantiateRow(object window)
        {
            if (_winDialogRow == null || _winLinesContainer == null) return null;
            var prefab = _winDialogRow.GetValue(window) as UnityEngine.Object;
            var parent = _winLinesContainer.GetValue(window) as Transform;
            if (prefab == null || parent == null) return null;
            return UnityEngine.Object.Instantiate(prefab, parent, worldPositionStays: false);
        }

        internal static void SetRowText(object row, string text)
        {
            SetTmpText(_rowText?.GetValue(row), text);
        }

        internal static void ResetRowHighlight(object row) => SetRowHighlight(row, false);

        internal static void SetRowHighlight(object row, bool on)
        {
            if (_rowHighlight == null) return;
            try { _rowHighlight.Invoke(row, new object[] { on }); }
            catch (Exception ex) { Plugin.Log.LogWarning("[DialogUiBinder] SetRowHighlight: " + ex.Message); }
        }

        internal static void SetOptionsInteractable(object window)
        {
            if (_winOptionsCanvasGroup?.GetValue(window) is CanvasGroup cg)
            {
                cg.interactable = true;
                cg.alpha = 1f;
                cg.blocksRaycasts = true;
            }
        }

        private static void SetTmpText(object? tmp, string text)
        {
            if (tmp == null) return;
            if (tmp is Component comp) comp.gameObject.SetActive(true);
            PropertyInfo? prop = tmp.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
            prop?.SetValue(tmp, text ?? "");
        }

        internal static void CloseActiveScreen()
        {
            object? screen = FindActiveScreen();
            // Stop any video background + free its RenderTexture before the VisitBg GameObject (which hosts the
            // VideoPlayer) is destroyed.
            StopVideo();
            // The screen is pooled (Close hides, doesn't destroy), so our full-screen VisitBg would linger and
            // reappear on the next pooled show. Destroy it before closing.
            if (screen is Component sc)
            {
                Transform bg = sc.transform.Find("VisitBg");
                if (bg != null) UnityEngine.Object.Destroy(bg.gameObject);
                NeutralizeTmpForClose(sc);
            }
            // Native close: ScreenController.CloseScreen() pops the manager and returns to raid (cursor/input
            // restored by the manager). This is exactly what the dialog's own "leave"/ESC does.
            bool closed = NativeBinder.CloseActiveController(screen);
            if (!closed && _screenClose != null && screen != null)
            {
                try { _screenClose.Invoke(screen, null); }
                catch (Exception ex) { Plugin.Log.LogWarning("[DialogUiBinder] Close() fallback threw: " + ex.Message); }
            }
            Plugin.Log.LogInfo("[DialogUiBinder] dialog screen closed (native CloseScreen=" + closed + ")");
        }

        // EFT's dialog screen is pooled and deactivates on close. Any TMP text left with CJK fallback
        // glyphs owns a TMP_SubMeshUI child that throws NRE in OnDisable (its material is null during the
        // teardown race) — visible only on close, never on mid-dialog row destruction (screen stays active).
        // Clearing each active TMP to empty + ForceMeshUpdate disables those sub-meshes NOW, while the screen
        // is still active (the clean path), so CloseScreen's deactivation is silent.
        private static void NeutralizeTmpForClose(Component screen)
        {
            if (_tmpTextType == null || _tmpTextProp == null) return;
            foreach (Component t in screen.GetComponentsInChildren(_tmpTextType, true))
            {
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                try
                {
                    _tmpTextProp.SetValue(t, "");
                    if (_tmpForceMeshUpdate != null)
                    {
                        ParameterInfo[] ps = _tmpForceMeshUpdate.GetParameters();
                        object[] args = new object[ps.Length];
                        for (int i = 0; i < ps.Length; i++)
                            args[i] = ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null;
                        _tmpForceMeshUpdate.Invoke(t, args);
                    }
                }
                catch { }
            }
        }

        private static MethodInfo? FindMethodUp(Type? type, string name)
        {
            while (type != null && type != typeof(object))
            {
                MethodInfo? m = type.GetMethod(name, All, null, Type.EmptyTypes, null);
                if (m != null) return m;
                type = type.BaseType;
            }
            return null;
        }

        private static bool _dumpedHierarchy;

        internal static void SetBackground(string? relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return;
            string ext = Path.GetExtension(relativePath).ToLowerInvariant();
            bool isImage = ext == ".png" || ext == ".jpg" || ext == ".jpeg";
            bool isVideo = ext == ".mp4" || ext == ".webm" || ext == ".m4v" || ext == ".mov";
            if (!isImage && !isVideo)
            {
                Plugin.Log.LogInfo("[DialogUiBinder] background '" + relativePath + "' skipped (PNG/JPG/MP4 supported)");
                return;
            }
            if (!(FindActiveScreen() is Component screen))
            {
                Plugin.Log.LogWarning("[DialogUiBinder] background: no active screen");
                return;
            }
            if (!_dumpedHierarchy)
            {
                _dumpedHierarchy = true;
                DumpHierarchy(screen.transform, 0, 4);
            }
            RawImage? img = EnsureBgImage(screen.transform);
            if (img == null) return;

            string full = DialogTreeLoader.ResolveAsset(relativePath!);
            if (isVideo)
            {
                SetVideoBackground(img, full, relativePath!);
                return;
            }

            // Static image: stop any prior video loop, then show the texture.
            StopVideo();
            Texture2D? tex = LoadTexture(full);
            if (tex == null) return;
            img.texture = tex;
            img.color = Color.white;
            img.gameObject.SetActive(true);
            Plugin.Log.LogInfo("[DialogUiBinder] background set: " + relativePath);
        }

        // Looping video background: a VideoPlayer renders the mp4 into a RenderTexture shown on the VisitBg RawImage.
        // Unity's VideoModule decodes H.264 mp4 via Media Foundation on Windows. Audio is muted (it's a background
        // loop); flip audioOutputMode to Direct if a node's video should be heard.
        private static void SetVideoBackground(RawImage img, string fullPath, string rel)
        {
            if (!File.Exists(fullPath))
            {
                Plugin.Log.LogWarning("[DialogUiBinder] video background not found: " + fullPath);
                return;
            }
            StopVideo();

            int w = Screen.width > 0 ? Screen.width : 1920;
            int h = Screen.height > 0 ? Screen.height : 1080;
            _bgRT = new RenderTexture(w, h, 0) { name = "VisitBgRT" };
            _bgRT.Create();

            VideoPlayer vp = img.gameObject.GetComponent<VideoPlayer>() ?? img.gameObject.AddComponent<VideoPlayer>();
            vp.source = VideoSource.Url;
            vp.url = fullPath;
            vp.renderMode = VideoRenderMode.RenderTexture;
            vp.targetTexture = _bgRT;
            vp.aspectRatio = VideoAspectRatio.Stretch;
            vp.isLooping = true;
            vp.playOnAwake = false;
            vp.waitForFirstFrame = true;
            vp.skipOnDrop = true;
            vp.audioOutputMode = VideoAudioOutputMode.None;
            _bgVideo = vp;

            img.texture = _bgRT;
            img.color = Color.white;
            img.gameObject.SetActive(true);
            vp.Play();
            Plugin.Log.LogInfo("[DialogUiBinder] video background playing: " + rel);
        }

        // Stop the current video loop and free its RenderTexture. The VideoPlayer component is left on VisitBg for
        // reuse (it's destroyed with VisitBg when the dialog closes).
        private static void StopVideo()
        {
            if (_bgVideo != null)
            {
                try { _bgVideo.Stop(); _bgVideo.targetTexture = null; } catch { }
                _bgVideo = null;
            }
            if (_bgRT != null)
            {
                try { _bgRT.Release(); } catch { }
                UnityEngine.Object.Destroy(_bgRT);
                _bgRT = null;
            }
        }

        private static void DumpHierarchy(Transform t, int depth, int maxDepth)
        {
            if (depth > maxDepth) return;
            string pad = depth > 0 ? new string('.', depth * 2) : "";
            string tags = "";
            if (t.GetComponent<Image>() != null) tags += "Image ";
            if (t.GetComponent<RawImage>() != null) tags += "RawImage ";
            if (t.GetComponent<Canvas>() != null) tags += "Canvas ";
            if (t.GetComponent<CanvasGroup>() != null) tags += "CanvasGroup ";
            string size = t is RectTransform rt ? (int)rt.rect.width + "x" + (int)rt.rect.height : "";
            Plugin.Log.LogInfo("[BgDump] " + pad + "[" + t.GetSiblingIndex() + "] '" + t.name + "' act=" + t.gameObject.activeSelf + " " + tags + size);
            for (int i = 0; i < t.childCount; i++)
                DumpHierarchy(t.GetChild(i), depth + 1, maxDepth);
        }

        private static Texture2D? LoadTexture(string path)
        {
            if (_bgCache.TryGetValue(path, out Texture2D cached)) return cached;
            if (!File.Exists(path))
            {
                Plugin.Log.LogWarning("[DialogUiBinder] background not found: " + path);
                return null;
            }
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(File.ReadAllBytes(path)))
            {
                Plugin.Log.LogWarning("[DialogUiBinder] failed to decode background: " + path);
                return null;
            }
            _bgCache[path] = tex;
            return tex;
        }

        private static RawImage? EnsureBgImage(Transform parent)
        {
            Transform existing = parent.Find("VisitBg");
            RawImage? img = existing != null ? existing.GetComponent<RawImage>() : null;
            if (img != null) return img;
            GameObject go = new GameObject("VisitBg", typeof(RectTransform), typeof(RawImage));
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.SetAsFirstSibling();
            img = go.GetComponent<RawImage>();
            img.raycastTarget = false;
            return img;
        }
    }
}
