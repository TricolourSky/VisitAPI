using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace VisitAPI.Native;

public static class AudioFiles
{
    static readonly Dictionary<string, AudioClip> _cache = new();

    public static void Load(string file, Action<AudioClip> onDone)
    {
        if (_cache.TryGetValue(file, out var hit)) { onDone(hit); return; }
        var path = Path.Combine(DialogFiles.Loader.BaseDir, "audio", file);
        if (!File.Exists(path)) { Plugin.Log.LogWarning("[audio] file not found: " + path); return; }
        Plugin.Instance.StartCoroutine(Fetch(file, path, onDone));
    }

    static IEnumerator Fetch(string file, string path, Action<AudioClip> onDone)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var type = ext == ".ogg" ? AudioType.OGGVORBIS : ext == ".mp3" ? AudioType.MPEG : AudioType.WAV;
        using (var req = UnityWebRequestMultimedia.GetAudioClip("file:///" + path.Replace('\\', '/'), type))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) { Plugin.Log.LogWarning("[audio] load failed: " + req.error); yield break; }
            var clip = DownloadHandlerAudioClip.GetContent(req);
            _cache[file] = clip;
            onDone(clip);
        }
    }

    public static void ReleaseAll()
    {
        foreach (var clip in _cache.Values) UnityEngine.Object.Destroy(clip);
        _cache.Clear();
    }
}
