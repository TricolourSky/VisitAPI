using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using EFT.AnimationSequencePlayer;
using Newtonsoft.Json.Linq;
using uLipSync;
using UnityEngine;
using UnityEngine.Networking;

namespace VisitAPI.Scene
{
    // Voice lines for raw-pack visits. Retail plays each line's voice through the lipSync track of
    // GClass4065.RunCombinedSequence: LipSyncDictionary maps the line's lipSyncId to a uLipSync
    // BakedData (audio clip + baked mouth frames) that uLipSyncBakedDataPlayer plays. The BakedData
    // assets died in the AssetRipper round-trip (encrypted MonoBehaviour fields), but the AUDIO
    // clips were re-extracted to loose wavs (pack `voices\` + `voice_map.json`, built by
    // _ripwork\harvest_voices.ps1 + gen_voice_map.ps1). Rebuild the chain at runtime: BakedData
    // instances carry only audioClip+duration (no mouth frames — GetFrame safely returns zero), so
    // the native track plays voice with retail timing/volume, just without lip movement.
    //
    // The wiring is decompile-verified: SequenceReader.Awake caches GetComponent<uLipSyncBakedDataPlayer>
    // + GetComponent<LipSyncDictionary> from its OWN GameObject (the trader), and RunCombinedSequence
    // resolves each line's lipSyncId against that dictionary and calls player.Play(bakedData). Two
    // audibility traps had to be closed: (1) the player self-stops the frame `dspTime-_startTime >
    // bakedData.duration`, so a not-yet-loaded clip (duration 0) is cut instantly and never retried —
    // the opening greeting fires ~1 frame after Stage, long before an async wav decode finishes, so it
    // was silent; SceneStage gates the dialog open on AllClipsReady, and we seed a non-zero placeholder
    // duration as insurance. (2) PlayAudioSource does GetComponent<AudioSource>() (root only) or creates
    // one — we pre-create a dedicated 2D source so voice is immune to the +300y scene lift / 3D rolloff.
    internal static class RawSceneVoice
    {
        private static JObject? _map;
        private static bool _loadTried;
        private static readonly List<AudioClip> _loadedClips = new List<AudioClip>();
        private static int _generation;
        private static int _pending;

        // True once every wired clip for the current scene has finished decoding (or there were none).
        // SceneStage waits on this before opening the native dialog so the opening greeting is audible.
        internal static bool AllClipsReady => _pending <= 0;

        // Must run BEFORE SequenceReader is added — its Awake caches the dictionary + player.
        internal static void Prepare(GameObject host, string ownerId)
        {
            _pending = 0;
            if (!(LoadMap()?[ownerId] is JObject entries) || !entries.HasValues) return;

            LipSyncDictionary dict = host.AddComponent<LipSyncDictionary>();
            uLipSyncBakedDataPlayer player = host.AddComponent<uLipSyncBakedDataPlayer>();
            player.playOnAwake = false;

            // Dedicated 2D source: PlayAudioSource would otherwise GetComponent<AudioSource>() on the root
            // (grabbing a stray 3D leftover) or add a default one; forcing spatialBlend=0 routes voice
            // straight to the AudioListener regardless of the +300y lift or listener distance.
            AudioSource src = host.AddComponent<AudioSource>();
            src.spatialBlend = 0f;
            src.dopplerLevel = 0f;
            src.loop = false;
            src.playOnAwake = false;
            src.outputAudioMixerGroup = null;
            player.audioSource = src;

            int generation = _generation;
            int wired = 0, missing = 0;
            foreach (KeyValuePair<string, JToken?> pair in entries)
            {
                string? file = pair.Value?.ToString();
                if (string.IsNullOrEmpty(file)) continue;
                string path = Path.Combine(SceneAssets.VendorsDir, "voices", file);
                if (!File.Exists(path))
                {
                    missing++;
                    continue;
                }
                BakedData data = ScriptableObject.CreateInstance<BakedData>();
                data.name = pair.Key;
                // Non-zero until the real clip.length lands, so a just-in-time play is never self-cut.
                data.duration = 60f;
                dict.Add(pair.Key, new LipSyncElement { bakedData = data, volume = 1f });
                _pending++;
                Plugin.Instance.StartCoroutine(LoadClip(path, data, generation));
                wired++;
            }
            Plugin.Log.LogInfo("[RawSceneVoice] " + wired + " voice line(s) wired for " + ownerId
                + (missing > 0 ? " (" + missing + " wav(s) missing on disk)" : ""));
        }

        private static IEnumerator LoadClip(string path, BakedData target, int generation)
        {
            AudioType type = path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ? AudioType.OGGVORBIS : AudioType.WAV;
            UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, type);
            try
            {
                yield return request.SendWebRequest();
                if (generation != _generation) yield break;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Plugin.Log.LogWarning("[RawSceneVoice] " + Path.GetFileName(path) + ": " + request.error);
                    yield break;
                }
                AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
                if (clip == null) yield break;
                clip.name = target.name;
                target.audioClip = clip;
                target.duration = clip.length;
                _loadedClips.Add(clip);
            }
            finally
            {
                request.Dispose();
                // Only settle the counter for the live scene — a stale coroutine from a closed visit
                // must not drive the current scene's readiness negative.
                if (generation == _generation) _pending--;
            }
        }

        // Scene teardown: abandon in-flight loads and free the decoded clips (a full trader's set is
        // tens of MB of PCM — must not pile up across visits).
        internal static void Unload()
        {
            _generation++;
            _pending = 0;
            foreach (AudioClip clip in _loadedClips)
                if (clip != null) UnityEngine.Object.Destroy(clip);
            _loadedClips.Clear();
        }

        private static JObject? LoadMap()
        {
            if (_loadTried) return _map;
            _loadTried = true;
            string path = Path.Combine(SceneAssets.VendorsDir, "voice_map.json");
            if (!File.Exists(path))
            {
                Plugin.Log.LogWarning("[RawSceneVoice] voice_map.json missing: " + path + " — visits stay silent");
                return null;
            }
            try
            {
                _map = JObject.Parse(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[RawSceneVoice] voice_map.json: " + ex.Message);
            }
            return _map;
        }
    }
}
