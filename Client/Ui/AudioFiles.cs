using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace VisitAPI.Native;

/// <summary>.dlg 的 audio / bgm（config\VisitAPI\audio\ 下的文件）：按文件名缓存，一场对话结束整批释放（DialogAudio.OnDestroy）。
/// 10-24：从文件解 AudioClip 的那一段（Decode）三处共用——这里、VoiceCache（商人台词语音，整局缓存）、RaidVoice（战局区域语音，整局缓存）。</summary>
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
        AudioClip clip = null;
        yield return Decode(path, c => clip = c, err => Plugin.Log.LogWarning("[audio] load failed: " + err));
        if (clip == null) yield break;
        if (_cache.TryGetValue(file, out var first)) { UnityEngine.Object.Destroy(clip); clip = first; }   // 同一文件并发加载：留先到的，后到的销毁（被盖掉的那份没人释放，10-29）
        else _cache[file] = clip;
        onDone(clip);
    }

    /// <summary>本地音频文件 → AudioClip（UnityWebRequestMultimedia）。格式按扩展名：.wav → WAV、.mp3 → MPEG、其余（.ogg 等）→ OGGVORBIS。
    /// 请求失败：fail 收到原因、done 收到 null；解出来给 done（引擎解不动时也可能是 null，调用方自己判）。缓存寿命各调用方自己管。</summary>
    public static IEnumerator Decode(string path, Action<AudioClip> done, Action<string> fail = null)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var type = ext == ".wav" ? AudioType.WAV : ext == ".mp3" ? AudioType.MPEG : AudioType.OGGVORBIS;
        using (var req = UnityWebRequestMultimedia.GetAudioClip("file:///" + path.Replace('\\', '/'), type))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) { fail?.Invoke(req.error); done(null); yield break; }
            done(DownloadHandlerAudioClip.GetContent(req));
        }
    }

    public static void ReleaseAll()
    {
        foreach (var clip in _cache.Values) UnityEngine.Object.Destroy(clip);
        _cache.Clear();
    }
}
