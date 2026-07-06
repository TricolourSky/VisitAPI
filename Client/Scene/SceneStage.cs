using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.AnimationSequencePlayer;
using EFT.UI;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using VisitAPI.Scene.RetailReplay;
using UnityScene = UnityEngine.SceneManagement.Scene;

namespace VisitAPI.Scene
{
    // Stages a vendor scene: loads the bundle, lifts the scene to y=300 (clear of the hideout), fixes
    // shaders, points a dedicated camera at the discovered CameraPoint and hides the menu environment —
    // the same staging bmpq's tradermod does. Two entries share it: OpenRetail (retail replay — the native
    // dialog engine opens on top) and OpenForDialog (a .dlg with a `scene:` header — the .dlg engine drives
    // the window on top). Menu-side only, never over a live raid.
    internal static class SceneStage
    {
        private static string _currentTraderId = "";
        private static string _currentSceneName = "";
        private static VendorScene? _scene;
        private static Camera? _cam;
        private static bool _busy;
        private static bool _closeRequested;

        internal static bool IsOpen => _currentTraderId.Length > 0;
        internal static bool IsBusy => _busy;
        internal static string CurrentTraderId => _currentTraderId;
        internal static Animator? CurrentTraderAnimator => _scene?.TraderAnimator;

        private static string ScenesDir => Path.Combine(DialogTreeLoader.BaseDir, "scenes");

        internal static bool EnsureAssetsResolved()
        {
            if (SceneAssets.Root.Length > 0) return true;
            return SceneAssets.Resolve(Plugin.SceneAssetsRoot.Value);
        }

        internal static void TryOpenRetail(string traderId)
        {
            if (!EnsureAssetsResolved()) return;
            if (InRaid())
            {
                Plugin.Log.LogWarning("[SceneStage] refusing to open a vendor scene in raid");
                return;
            }
            Plugin.Instance.StartCoroutine(OpenRetail(traderId));
        }

        // Vendor scenes stage a separate world and repurpose the camera — menu/hideout only, never over a
        // live raid (the retail system has the same restriction, for the same reason).
        private static bool InRaid()
            => Singleton<AbstractGame>.Instantiated && Singleton<AbstractGame>.Instance.InRaid;

        internal static IEnumerator OpenRetail(string traderId)
        {
            if (_busy) yield break;
            _busy = true;
            _closeRequested = false;
            // yield-break paths fall through to the finally — a thrown exception inside the coroutine body
            // must ALSO release _busy, or every later open is silently ignored for the rest of the session.
            try
            {
                Plugin.Log.LogInfo("[SceneStage] opening retail vendor scene for " + traderId);

                if (!SceneAssets.Bind()) yield break;

                if (IsOpen)
                {
                    IEnumerator close = CloseInternal();
                    while (close.MoveNext()) yield return close.Current;
                }

                SceneAssets.GetBundle(SceneAssets.SharedBundleFile);
                string? bundleFile = SceneAssets.FindVendorBundleFile(traderId);
                AssetBundle? bundle = bundleFile != null ? SceneAssets.GetBundle(bundleFile) : null;
                if (bundle == null)
                {
                    Plugin.Log.LogWarning("[SceneStage] no scene bundle for " + traderId);
                    yield break;
                }

                IEnumerator stage = Stage(bundle, traderId);
                while (stage.MoveNext()) yield return stage.Current;
                if (!IsOpen) yield break;

                // The native opening greeting fires ~1 frame after the dialog opens — wait for the voice
                // clips to finish decoding first, or that first line self-stops silent (its BakedData
                // duration is still 0 → the player cuts it on the next frame and never retries). Clicked
                // lines seconds later already have loaded clips, so only the greeting needs this gate.
                if (SceneAssets.Layout == SceneAssetsLayout.RawPack)
                {
                    float waited = 0f;
                    while (!RawSceneVoice.AllClipsReady && waited < 4f)
                    {
                        yield return null;
                        waited += Time.unscaledDeltaTime;
                    }
                    if (!RawSceneVoice.AllClipsReady)
                        Plugin.Log.LogWarning("[SceneStage] voice clips still loading after " + waited.ToString("F1") + "s — opening line may be silent");
                }

                if (!RetailDialogEngine.TryOpenNativeDialog(traderId, _scene?.TraderAnimator))
                {
                    Play("Greetings");
                    // Raw scenes have no bmpq chatter tables to fall back on — a visit without a dialog
                    // would strand the player in an empty room (the button gating normally prevents this;
                    // F9 cycling can still get here), so tear the scene back down.
                    if (_scene?.TraderSceneComp == null)
                    {
                        Plugin.Log.LogWarning("[SceneStage] no dialog data for " + traderId + " — closing scene");
                        RequestDeferredClose();
                    }
                }
            }
            finally
            {
                _busy = false;
                if (_closeRequested)
                {
                    _closeRequested = false;
                    Close();
                }
            }
        }

        // A .dlg declared `scene:` — stage the room only; DialogRunner renders the window content on top
        // (the scene IS the backdrop, `bg:` is skipped while it's up). Failing to stage is non-fatal: the
        // dialog just keeps its flat background.
        internal static IEnumerator OpenForDialog(string sceneRef, string traderId)
        {
            if (_busy) yield break;
            _busy = true;
            _closeRequested = false;
            try
            {
                if (InRaid())
                {
                    Plugin.Log.LogWarning("[SceneStage] refusing to open a vendor scene in raid");
                    yield break;
                }

                if (IsOpen)
                {
                    IEnumerator close = CloseInternal();
                    while (close.MoveNext()) yield return close.Current;
                }

                AssetBundle? bundle = ResolveDialogSceneBundle(sceneRef);
                if (bundle == null) yield break;

                IEnumerator stage = Stage(bundle, traderId);
                while (stage.MoveNext()) yield return stage.Current;
            }
            finally
            {
                _busy = false;
                if (_closeRequested)
                {
                    _closeRequested = false;
                    Close();
                }
            }
        }

        // `scene: <ref>` resolution: a bundle FILE dropped in config/VisitAPI/scenes/ (author custom scene
        // or a raw retail rebuild; a vendors_shared.bundle beside it preloads first), else a 24-hex trader
        // id looked up in the retail asset pack (borrow a retail vendor's room).
        private static AssetBundle? ResolveDialogSceneBundle(string sceneRef)
        {
            string path = Path.Combine(ScenesDir, sceneRef);
            if (File.Exists(path))
            {
                string shared = Path.Combine(ScenesDir, "vendors_shared.bundle");
                if (File.Exists(shared)) SceneAssets.GetBundleAtPath(shared);
                return SceneAssets.GetBundleAtPath(path);
            }
            if (sceneRef.Length == 24 && EnsureAssetsResolved())
            {
                SceneAssets.Bind();
                SceneAssets.GetBundle(SceneAssets.SharedBundleFile);
                string? file = SceneAssets.FindVendorBundleFile(sceneRef);
                if (file != null) return SceneAssets.GetBundle(file);
            }
            Plugin.Log.LogWarning("[SceneStage] scene '" + sceneRef + "' not found (no scenes\\ file, no vendor bundle) — dialog continues without a scene");
            return null;
        }

        private static IEnumerator Stage(AssetBundle bundle, string traderId)
        {
            if (!bundle.isStreamedSceneAssetBundle || bundle.GetAllScenePaths().Length == 0)
            {
                Plugin.Log.LogWarning("[SceneStage] bundle has no scene: " + bundle.name);
                yield break;
            }

            string sceneName = Path.GetFileNameWithoutExtension(bundle.GetAllScenePaths()[0]);
            UnityScene existing = SceneManager.GetSceneByName(sceneName);
            if (!existing.IsValid() || !existing.isLoaded)
            {
                AsyncOperation op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
                while (!op.isDone) yield return null;
            }

            UnityScene scene = SceneManager.GetSceneByName(sceneName);
            GameObject[] roots = scene.IsValid() ? scene.GetRootGameObjects() : Array.Empty<GameObject>();
            if (roots.Length == 0)
            {
                Plugin.Log.LogError("[SceneStage] scene '" + sceneName + "' has no root objects");
                if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
                yield break;
            }

            // Raw retail scenes have MANY roots at their real map coords (Skier sits at x=321) — translate
            // each root up by 300 instead of snapping one root to (0,300,0) so relative placement survives.
            // bmpq's single origin-rooted scenes land on the same (0,300,0) either way.
            foreach (GameObject root in roots)
            {
                ReplaceShadersToNative(root);
                root.transform.position += new Vector3(0f, 300f, 0f);
            }
            if (SceneAssets.Layout == SceneAssetsLayout.RawPack) SanitizeRawSceneObjects(roots, sceneName);
            SceneManager.SetActiveScene(scene);
            // Raw-pack only: its skybox is a broken dummy and its baked lighting was lost in the AR
            // round-trip, so we swap the sky shader + drive ambient ourselves. bmpq's pack keeps its own
            // working skybox + baked lighting — touching either just breaks his intended look.
            if (SceneAssets.Layout == SceneAssetsLayout.RawPack)
            {
                FixSkyboxShader();
                ApplyRawLighting(sceneName);
            }
            Plugin.Log.LogInfo("[SceneStage] lightmaps: " + LightmapSettings.lightmaps.Length
                + ", light probes: " + (LightmapSettings.lightProbes != null ? LightmapSettings.lightProbes.count : 0));

            _scene = VendorSceneSource.Discover(roots);
            if (_scene.TraderSceneComp == null && _scene.TraderAnimator != null)
                RawSceneAnimation.Prepare(_scene.TraderAnimator, sceneName);
            _currentSceneName = sceneName;
            _currentTraderId = traderId;

            if (SceneAssets.Layout == SceneAssetsLayout.RawPack)
                RawSceneAmbience.Prepare(roots, traderId);

            if (_scene.CameraPoint != null) SetupCamera(_scene.CameraPoint);
            else Plugin.Log.LogWarning("[SceneStage] scene has no camera point");

            SetMenuEnvironmentVisible(false);
            Plugin.Log.LogInfo("[SceneStage] scene '" + sceneName + "' up for trader " + traderId);
        }

        // Quit raised while the screen is still queued (a line died during the opening StartDialog): the
        // screen never subscribed, so nothing closes it natively and Close() would no-op against _busy.
        // Defer to the end of Open, which closes screen + scene.
        internal static void RequestDeferredClose() => _closeRequested = true;

        internal static void Close()
        {
            if (_busy || !IsOpen) return;
            Plugin.Instance.StartCoroutine(CloseInternal());
        }

        private static IEnumerator CloseInternal()
        {
            Plugin.Log.LogInfo("[SceneStage] closing " + _currentSceneName);
            RetailDialogEngine.CloseDialog();
            RawSceneVoice.Unload();
            RawSceneAmbience.Unload();
            if (_scene?.TraderSceneComp != null)
            {
                PlayableDirector? director = SceneAssets.GetDirector(_scene.TraderSceneComp);
                if (director != null) director.Stop();
            }
            try { GClass4065.StopSequence(); } catch { }

            _scene = null;
            _currentTraderId = "";

            if (_currentSceneName.Length > 0)
            {
                UnityScene scene = SceneManager.GetSceneByName(_currentSceneName);
                _currentSceneName = "";
                if (scene.IsValid() && scene.isLoaded)
                {
                    AsyncOperation op = SceneManager.UnloadSceneAsync(scene);
                    while (op != null && !op.isDone) yield return null;
                }
            }

            if (_cam != null) _cam.gameObject.SetActive(false);
            if (CameraClass.Instance != null && CameraClass.Instance.Camera != null)
                CameraClass.Instance.IsActive = true;
            SetMenuEnvironmentVisible(true);
        }

        // Play one animation of the given dialog type: a scene timeline when the scene carries one for that
        // type, otherwise a retail dialogue.json line through the native SequenceReader on the trader model.
        // Both need bmpq's TraderScene data — raw/custom scenes have no chatter tables, so this no-ops there.
        internal static void Play(string dialogTypeName)
        {
            Component? comp = _scene?.TraderSceneComp;
            if (comp == null || SceneAssets.DialogTypeEnum == null) return;
            object key;
            try { key = Enum.Parse(SceneAssets.DialogTypeEnum, dialogTypeName, true); }
            catch
            {
                Plugin.Log.LogWarning("[SceneStage] unknown dialog type '" + dialogTypeName + "'");
                return;
            }

            PlayableDirector? director = SceneAssets.GetDirector(comp);
            System.Collections.IDictionary? timelines = SceneAssets.GetTimelineDialogs(comp);
            if (director != null && timelines != null && timelines.Contains(key)
                && timelines[key] is System.Collections.IList timelineList && timelineList.Count > 0)
            {
                if (timelineList[UnityEngine.Random.Range(0, timelineList.Count)] is PlayableAsset asset)
                {
                    director.Stop();
                    try { GClass4065.StopSequence(); } catch { }
                    director.playableAsset = asset;
                    director.Play();
                    Plugin.Log.LogInfo("[SceneStage] timeline '" + asset.name + "' (" + dialogTypeName + ")");
                    return;
                }
            }

            System.Collections.IDictionary? dialogs = SceneAssets.GetDialogs(comp);
            if (dialogs == null || !dialogs.Contains(key) || !(dialogs[key] is List<string> ids) || ids.Count == 0)
            {
                Plugin.Log.LogInfo("[SceneStage] no '" + dialogTypeName + "' content in this scene");
                return;
            }
            string lineId = ids[UnityEngine.Random.Range(0, ids.Count)];
            GClass3666? line = RetailDialogEngine.GetLine(lineId);
            if (line == null)
            {
                Plugin.Log.LogWarning("[SceneStage] line '" + lineId + "' not in dialogue.json");
                return;
            }
            Animator? animator = _scene?.TraderAnimator;
            SequenceReader? reader = animator != null ? animator.GetComponent<SequenceReader>() : null;
            if (reader == null)
            {
                Plugin.Log.LogWarning("[SceneStage] trader model has no SequenceReader");
                return;
            }
            if (director != null) director.Stop();
            try { GClass4065.StopSequence(); } catch { }
            reader.Play(line.AnimationData).ContinueWith(t => Plugin.Log.LogInfo(t.IsFaulted
                ? "[SceneStage] sequence faulted: " + t.Exception?.InnerException?.Message
                : "[SceneStage] sequence done: " + lineId));
            Plugin.Log.LogInfo("[SceneStage] native sequence '" + lineId + "' (" + dialogTypeName + ")");
        }

        // The hideout FPS camera prefab, exactly as tradermod stages it: Cinemachine off, 60° FOV, black
        // clear color. The main game camera is switched off while the vendor scene is up.
        private static void SetupCamera(Transform camPoint)
        {
            if (_cam == null)
            {
                GameObject prefab = Resources.Load<GameObject>("Cam2_fps_hideout");
                if (prefab == null)
                {
                    Plugin.Log.LogError("[SceneStage] camera prefab 'Cam2_fps_hideout' not found");
                    return;
                }
                GameObject go = UnityEngine.Object.Instantiate(prefab);
                go.name = "VisitSceneCamera";
                _cam = go.GetComponent<Camera>();
                if (go.GetComponent("CinemachineBrain") is Behaviour brain) brain.enabled = false;
                _cam.fieldOfView = 60f;
                _cam.backgroundColor = Color.black;
                _cam.clearFlags = CameraClearFlags.SolidColor;
                // Switching CameraClass.Instance off (below) deactivates the FPS camera's AudioListener,
                // so the vendor camera must carry one or nothing is audible. The Cam2_fps_hideout prefab
                // usually ships one (bmpq's identical staging is audible) — add defensively only if not.
                if (_cam.GetComponent<AudioListener>() == null)
                    _cam.gameObject.AddComponent<AudioListener>();
                if (Plugin.SceneCameraPostFx.Value) EnableCameraPostFx(go);
                UnityEngine.Object.DontDestroyOnLoad(go);
            }
            if (CameraClass.Instance != null && CameraClass.Instance.Camera != null)
                CameraClass.Instance.IsActive = false;
            _cam.gameObject.SetActive(true);
            _cam.transform.SetPositionAndRotation(camPoint.position, camPoint.rotation);
        }

        // Opt-in (default off) EFT camera grading, mirroring bmpq's TraderCameraController: turn on
        // PrismEffects exposure/tonemapping and the CC_FastVignette for the cinematic retail tone.
        // Reflected (we don't reference those assemblies) and non-fatal; off by default so it never
        // overrides a user who has already tuned the raw-pack ambient by hand.
        private static void EnableCameraPostFx(GameObject camGo)
        {
            try
            {
                foreach (Component c in camGo.GetComponentsInChildren<Component>(true))
                {
                    if (c == null) continue;
                    string tn = c.GetType().Name;
                    if (tn == "PrismEffects") SetMember(c, "useExposure", true);
                    else if (tn == "CC_FastVignette")
                    {
                        if (c is Behaviour b) b.enabled = true;
                        SetMember(c, "darkness", 44f);
                    }
                }
                Plugin.Log.LogInfo("[SceneStage] camera post-fx (exposure + vignette) enabled");
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[SceneStage] camera post-fx: " + ex.Message); }
        }

        private static void SetMember(Component c, string name, object value)
        {
            Type t = c.GetType();
            FieldInfo? f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null && f.FieldType == value.GetType()) { f.SetValue(c, value); return; }
            PropertyInfo? p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && p.CanWrite && p.PropertyType == value.GetType()) p.SetValue(c, value);
        }

        private static void SetMenuEnvironmentVisible(bool visible)
        {
            EnvironmentUI env = Singleton<EnvironmentUI>.Instance;
            if (env == null) return;
            env.ShowEnvironment(visible);
            env.EnableOverlay(false);
        }

        // AssetRipper cannot rebuild compiled shaders, so every material in the raw pack carries a
        // broken stand-in under the original shader name — swap in the game's own shader of the same
        // name (snapshotted pre-bundle-load; see SceneAssets). Names with no native counterpart are
        // logged once each: that list is the flat/grey-material suspect list.
        private static readonly HashSet<string> _reportedMissingShaders = new HashSet<string>();

        // Character/prop shaders that never resolve by name (hair/cloth/rocks are not resident in the
        // pre-bundle menu snapshot) → remap onto the always-resident p0/ family so they render lit and
        // cut-out instead of a wrong Unity-Standard BRDF (grey/opaque). Resolved against the snapshot
        // only, which is dummy-free by construction. See DEV_NOTES #39.
        private static readonly Dictionary<string, string[]> _shaderRemap = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Characters/TraiderHair"] = new[] { "p0/Reflective/Bumped Specular SMap Transparent Cutoff", "p0/Cutout/Bumped Diffuse", "p0/Reflective/Bumped Specular SMap" },
            ["Cloth/ClothShader"] = new[] { "p0/Reflective/Bumped Specular SMap" },
            ["Cloth/ClothShader_backface"] = new[] { "p0/Reflective/Bumped Specular SMap" },
            ["ANGRYMESH/PBR Rocks/PBR BlendTopDetail (Legacy)"] = new[] { "p0/Reflective/Bumped Specular SMap" },
        };

        private static void ReplaceShadersToNative(GameObject root)
        {
            bool rawPack = SceneAssets.Layout == SceneAssetsLayout.RawPack;
            int replaced = 0, remapped = 0, fallback = 0, hiddenFx = 0, kept = 0;
            List<string>? newMisses = null;
            foreach (Renderer rend in root.GetComponentsInChildren<Renderer>(true))
            {
                if (rend == null) continue;
                foreach (Material mat in rend.sharedMaterials)
                {
                    if (mat == null || mat.shader == null) continue;
                    string shaderName = mat.shader.name;
                    Shader? native = SceneAssets.FindNativeShader(shaderName);
                    if (native != null)
                    {
                        if (mat.shader != native)
                        {
                            mat.shader = native;
                            replaced++;
                        }
                        continue;
                    }
                    // bmpq's pack ships WORKING shaders the menu snapshot can't see (the room's
                    // Unlit/Texture equi-panorama dome, plus stencil/fog/particle helpers he compiled) —
                    // KEEP them, exactly as his own mod does. Only the raw pack's shaders are all-broken
                    // AR dummies that need hiding/Standard; applying that to bmpq's pack greyed out the
                    // whole vendor room (the unlit photo dome turned into a lit Standard slab).
                    if (!rawPack)
                    {
                        if (_reportedMissingShaders.Add(shaderName))
                            (newMisses ??= new List<string>()).Add(shaderName);
                        kept++;
                        continue;
                    }

                    // Known-name remap onto a resident p0/ shader before any blunt fallback.
                    if (TryRemapShader(mat, shaderName)) { remapped++; continue; }

                    if (_reportedMissingShaders.Add(shaderName))
                        (newMisses ??= new List<string>()).Add(shaderName);
                    // A material whose shader has no native counterpart renders as an opaque grey slab.
                    // Fog/smoke sheets are pure set dressing — hide them; anything else (hair, cloth,
                    // rocks) gets Standard as a visible-but-sane stand-in.
                    string lower = shaderName.ToLowerInvariant();
                    if (lower.Contains("smoke") || lower.Contains("fog") || lower.Contains("haze")
                        || lower.Contains("billboard") || lower.Contains("particle"))
                    {
                        if (rend.enabled)
                        {
                            rend.enabled = false;
                            hiddenFx++;
                        }
                    }
                    else
                    {
                        Shader? standard = SceneAssets.FindNativeShader("Standard");
                        if (standard != null && mat.shader != standard)
                        {
                            mat.shader = standard;
                            fallback++;
                        }
                    }
                }
            }
            if (replaced > 0 || remapped > 0 || kept > 0)
                Plugin.Log.LogInfo("[SceneStage] replaced " + replaced + " shader(s) with native"
                    + (remapped > 0 ? ", " + remapped + " remapped to p0/" : "")
                    + (fallback > 0 ? ", " + fallback + " fell back to Standard" : "")
                    + (hiddenFx > 0 ? ", " + hiddenFx + " fx renderer(s) hidden" : "")
                    + (kept > 0 ? ", " + kept + " kept as-is (bmpq shaders)" : ""));
            if (newMisses != null)
                Plugin.Log.LogWarning("[SceneStage] no native shader for: " + string.Join(", ", newMisses.ToArray()));
        }

        // Swap a material off its (lost) named shader onto the first resident p0/ candidate, carrying
        // properties the target can't read by name: rock albedo/normal live under _Base* slots, and hair
        // is alpha-cutout that must have cutout turned on if it lands on the non-cutout SMap variant.
        private static bool TryRemapShader(Material mat, string shaderName)
        {
            if (!_shaderRemap.TryGetValue(shaderName, out string[] candidates)) return false;
            Shader? target = null;
            foreach (string name in candidates)
            {
                target = SceneAssets.FindNativeShader(name);
                if (target != null) break;
            }
            if (target == null) return false;

            if (shaderName.StartsWith("ANGRYMESH", StringComparison.Ordinal))
            {
                Texture? albedo = mat.HasProperty("_BaseAlbedoASmoothness") ? mat.GetTexture("_BaseAlbedoASmoothness") : null;
                Texture? normal = mat.HasProperty("_BaseNormalMap") ? mat.GetTexture("_BaseNormalMap") : null;
                mat.shader = target;
                if (albedo != null && mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", albedo);
                if (normal != null && mat.HasProperty("_BumpMap")) mat.SetTexture("_BumpMap", normal);
            }
            else
            {
                mat.shader = target;
                if (shaderName == "Characters/TraiderHair" && mat.HasProperty("_UseCutoff"))
                {
                    mat.SetFloat("_UseCutoff", 1f);
                    mat.EnableKeyword("USE_CUTOFF");
                    mat.renderQueue = 2450;
                }
            }
            return true;
        }

        // Two families of scene objects only behave with their original (unrecoverable) pieces:
        //   - `*_Stencil*` meshes — retail's view-mask (a stencil-only shader frames the room like a
        //     photo vignette). With the shader lost they render as a giant sepia slab over half the
        //     screen — THE washed-out overlay of the 0.8.x tests. No shader in 0.16.9 replaces them,
        //     so turn their renderers off; the room behind renders normally.
        //   - SUMMER/WINTER season variant roots — the season-picker script was stripped, so BOTH
        //     render at once (snow piles inside summer scenes). Keep SUMMER, sleep WINTER.
        private static void SanitizeRawSceneObjects(GameObject[] roots, string sceneName)
        {
            // Verified across all 8 vendor .unity inputs: only Prapor ships its stencil renderer enabled
            // (the sepia slab); the others already ship it m_Enabled:0, and Jaeger/Peacekeeper have none.
            // The parent stencil holder carries no renderer (mesh is on the *_LOD0 child), and both names
            // match, so dedupe renderers and print the full breakdown — '0 disabled' now clearly reads as
            // 'already off' rather than a missed match, which matters for future scene packs.
            int stencilGOs = 0, stencilRenderers = 0, stencilDisabled = 0, stencilAlreadyOff = 0, winters = 0;
            HashSet<Renderer> seen = new HashSet<Renderer>();
            foreach (GameObject root in roots)
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    string name = t.name;
                    if (name.IndexOf("stencil", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        stencilGOs++;
                        foreach (Renderer rend in t.GetComponentsInChildren<Renderer>(true))
                        {
                            if (rend == null || !seen.Add(rend)) continue;
                            stencilRenderers++;
                            if (rend.enabled) { rend.enabled = false; stencilDisabled++; }
                            else stencilAlreadyOff++;
                        }
                    }
                    else if (string.Equals(name, "WINTER", StringComparison.Ordinal))
                    {
                        if (t.gameObject.activeSelf) { t.gameObject.SetActive(false); winters++; }
                    }
                    else if (string.Equals(name, "SUMMER", StringComparison.Ordinal) && !t.gameObject.activeSelf)
                    {
                        t.gameObject.SetActive(true);
                    }
                }
            }
            Plugin.Log.LogInfo("[SceneStage] sanitize " + sceneName + ": stencil GOs=" + stencilGOs
                + ", renderers=" + stencilRenderers + " (disabled " + stencilDisabled + ", already-off " + stencilAlreadyOff
                + "), WINTER off=" + winters);
        }

        // The AssetRipper round-trip loses every baked lighting asset (light probes, reflection-probe
        // cubemaps). With the skybox shader fixed to a WORKING Skybox/Procedural, indoor rooms drown in
        // sky ambient + reflections and characters go black. Override: indoor scenes drop the skybox and
        // take a flat ambient (also feeds the ambient probe → faces lit), outdoor scenes keep a dimmed
        // sky; reflections are damped. RAW PACK ONLY and DORMANT — the shipped bmpq pack keeps its own
        // lighting and never runs this. The constants below were the shipping raw-pack tuning values; a
        // future self-made pack can lift them back into config if live tuning is wanted again.
        private const string RawOutdoorVendors = "jaeger,peacekeeper";
        private const float RawAmbientIntensity = 0.8f;
        private const float RawReflectionIntensity = 0.25f;
        private static readonly Color RawIndoorAmbient = new Color(0.32f, 0.32f, 0.35f);

        private static void ApplyRawLighting(string sceneName)
        {
            string vendor = sceneName.StartsWith("Vendors_", StringComparison.OrdinalIgnoreCase)
                ? sceneName.Substring("Vendors_".Length).ToLowerInvariant() : sceneName.ToLowerInvariant();
            bool outdoor = ("," + RawOutdoorVendors + ",").Contains("," + vendor + ",");
            if (outdoor)
            {
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
                RenderSettings.ambientIntensity = RawAmbientIntensity;
            }
            else
            {
                RenderSettings.skybox = null;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = RawIndoorAmbient;
            }
            RenderSettings.reflectionIntensity = RawReflectionIntensity;
            Plugin.Log.LogInfo("[SceneStage] raw lighting: " + (outdoor ? "outdoor" : "indoor") + " (dormant raw-pack defaults)");
        }

        private static void FixSkyboxShader()
        {
            Material sky = RenderSettings.skybox;
            if (sky == null || sky.shader == null) return;
            Shader? native = SceneAssets.FindNativeShader(sky.shader.name);
            if (native != null && sky.shader != native)
            {
                sky.shader = native;
                Plugin.Log.LogInfo("[SceneStage] skybox shader swapped to native (" + native.name + ")");
            }
        }
    }
}
