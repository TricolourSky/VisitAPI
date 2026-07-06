using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace VisitAPI.Scene
{
    // Room ambience for raw-pack visits. Each vendor scene kept its AmbientSoundPlayer / SOUND_<trader> /
    // Roomtone / REVERB_ROOM holder GameObjects, but their BSG driver scripts were stripped in the
    // AssetRipper round-trip and the AudioSources they fed have no clips, so the rooms are dead silent.
    // Rather than repair the stripped players, stream the harvested per-trader ambience loops onto a
    // fresh 2D AudioSource (spatialBlend 0 → host position irrelevant, no AudioListener placement
    // needed). ambience_map.json maps traderId → [loop filenames]; clips live in the pack's ambience\.
    // Same generation-guarded async streaming + clip disposal as RawSceneVoice (a single loop is
    // several MB of PCM and must not pile up across visits). Traders with no map entry stay silent.
    internal static class RawSceneAmbience
    {
        private static JObject? _map;
        private static bool _loadTried;
        private static readonly List<AudioClip> _loadedClips = new List<AudioClip>();
        private static readonly List<AudioSource> _sources = new List<AudioSource>();
        private static int _generation;
        private const float RawAmbienceVolume = 0.35f;   // dormant raw-pack default (was Scene.AmbienceVolume)

        internal static void Prepare(GameObject[] roots, string traderId)
        {
            if (roots.Length == 0) return;
            if (!(LoadMap()?[traderId] is JArray files) || files.Count == 0) return;

            GameObject holder = new GameObject("VisitAmbience");
            holder.transform.SetParent(roots[0].transform, false);

            int generation = _generation;
            float volume = RawAmbienceVolume;
            int wired = 0, missing = 0;
            foreach (JToken token in files)
            {
                string? file = token?.ToString();
                if (string.IsNullOrEmpty(file)) continue;
                string path = Path.Combine(SceneAssets.VendorsDir, "ambience", file);
                if (!File.Exists(path))
                {
                    missing++;
                    continue;
                }
                AudioSource src = holder.AddComponent<AudioSource>();
                src.loop = true;
                src.spatialBlend = 0f;
                src.dopplerLevel = 0f;
                src.playOnAwake = false;
                src.outputAudioMixerGroup = null;
                src.volume = volume;
                _sources.Add(src);
                Plugin.Instance.StartCoroutine(LoadClip(path, src, generation));
                wired++;
            }
            Plugin.Log.LogInfo("[RawSceneAmbience] " + wired + " ambience loop(s) for " + traderId
                + (missing > 0 ? " (" + missing + " clip(s) missing on disk)" : ""));
        }

        private static IEnumerator LoadClip(string path, AudioSource src, int generation)
        {
            AudioType type = path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ? AudioType.OGGVORBIS : AudioType.WAV;
            UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, type);
            try
            {
                yield return request.SendWebRequest();
                if (generation != _generation) yield break;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Plugin.Log.LogWarning("[RawSceneAmbience] " + Path.GetFileName(path) + ": " + request.error);
                    yield break;
                }
                AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
                if (clip == null || src == null) yield break;
                clip.name = Path.GetFileNameWithoutExtension(path);
                src.clip = clip;
                src.Play();
                _loadedClips.Add(clip);
            }
            finally
            {
                request.Dispose();
            }
        }

        internal static void Unload()
        {
            _generation++;
            foreach (AudioSource src in _sources)
                if (src != null) src.Stop();
            _sources.Clear();
            foreach (AudioClip clip in _loadedClips)
                if (clip != null) UnityEngine.Object.Destroy(clip);
            _loadedClips.Clear();
        }

        private static JObject? LoadMap()
        {
            if (_loadTried) return _map;
            _loadTried = true;
            string path = Path.Combine(SceneAssets.VendorsDir, "ambience_map.json");
            if (!File.Exists(path))
            {
                Plugin.Log.LogInfo("[RawSceneAmbience] ambience_map.json missing: " + path + " — rooms stay silent");
                return null;
            }
            try
            {
                _map = JObject.Parse(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[RawSceneAmbience] ambience_map.json: " + ex.Message);
            }
            return _map;
        }
    }
}
