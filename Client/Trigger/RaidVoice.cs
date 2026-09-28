using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;

namespace VisitAPI.Native;

public static class RaidVoice
{
    static readonly Dictionary<string, AudioClip> _clips = new();
    static readonly HashSet<string> _loading = new();

    public static void PlayAt(string file, Vector3 at, float volume = 0.8f, float maxDistance = 30f)
    {
        if (string.IsNullOrEmpty(file)) return;
        Plugin.Instance.StartCoroutine(Run(file, at, volume, maxDistance));
    }

    static IEnumerator Run(string file, Vector3 at, float volume, float maxDistance)
    {
        if (!_clips.TryGetValue(file, out var clip) || clip == null)
        {
            if (_loading.Contains(file)) { while (_loading.Contains(file)) yield return null; _clips.TryGetValue(file, out clip); }
            else
            {
                _loading.Add(file);
                try
                {
                    var path = Dump(file);
                    if (path == null) { Plugin.Log.LogWarning("[voice] embedded audio not found: " + file); yield break; }
                    using var req = UnityWebRequestMultimedia.GetAudioClip("file:///" + path.Replace('\\', '/'), file.EndsWith(".wav", System.StringComparison.OrdinalIgnoreCase) ? AudioType.WAV : AudioType.OGGVORBIS);
                    yield return req.SendWebRequest();
                    if (req.result != UnityWebRequest.Result.Success) { Plugin.Log.LogWarning($"[voice] audio load failed {file}: {req.error}"); yield break; }
                    clip = DownloadHandlerAudioClip.GetContent(req);
                    if (clip == null) { Plugin.Log.LogWarning("[voice] could not decode an AudioClip from audio: " + file); yield break; }
                    clip.name = file;
                    _clips[file] = clip;
                }
                finally { _loading.Remove(file); }
            }
        }
        if (clip == null) yield break;
        var go = new GameObject("VisitRaidVoice_" + file);
        go.transform.position = at;
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.spatialBlend = 1f;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.minDistance = 2f;
        src.maxDistance = maxDistance;
        src.volume = volume;
        src.dopplerLevel = 0f;
        src.playOnAwake = false;
        src.Play();
        Object.Destroy(go, clip.length + 0.5f);
    }

    static string Dump(string file)
    {
        var dir = Path.Combine(BepInEx.Paths.CachePath, "VisitAPI", "audio");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, file);
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("VisitAPI.art.audio." + file);
        if (stream == null) return null;
        if (File.Exists(path) && new FileInfo(path).Length == stream.Length) return path;
        using var fs = File.Create(path);
        stream.CopyTo(fs);
        return path;
    }
}
