using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EFT;
using EFT.AnimationSequencePlayer;
using EFT.Dialogs;
using EFT.GlobalEvents;
using EFT.UI;
using EFT.UI.Chat;
using EFT.UI.Screens;
using SPT.Common.Http;
using UnityEngine;
using UnityEngine.Networking;
using Object = UnityEngine.Object;

namespace VisitAPI.Native;

/// <summary>09-24 第三步：聊天里按「回复」（ViaNotebook / ViaRadio 的邀请）直接开原生对话屏放 1.1 的对话。
/// Kerman 这种没有房间的商人不走 narrate 场景，对话屏就盖在当前界面上；原生对话屏要一个 ITraderAnimationController 播商人的口型 / 字幕
/// （原生那个 TraderAnimationController 要场景里的 NPC），这里用 InvitePresenter 顶替：按台词的 lipSyncId 从服务端取语音播、字幕走原生的字幕事件。
/// 1.1 的笔记本 / 电台画面（DialogLaptopPresenter：猫面具视频 + 终端打字机字幕）这一步先不做。</summary>
public static class InviteDialog
{
    public static bool Open(PendingInvite p, ChatScreen screen)
    {
        var trader = string.IsNullOrEmpty(p.Mail.DialogueTrader) ? p.Trader : p.Mail.DialogueTrader;
        var dialogue = p.Mail.Dialogue;
        if (!TarkovApplication.Exist(out var app) || app.Session?.Profile == null) { Plugin.Log.LogWarning("[invite] Open dialogue: no session"); return false; }
        if (!(NarrateEntry.MenuOpField?.GetValue(app) is MainMenuShowOperation op) || op.QuestController == null || op.InventoryController == null) { Plugin.Log.LogWarning("[invite] Open dialogue: main menu not ready yet"); return false; }
        if (app.NarrateControllerAccess != null && app.NarrateControllerAccess.GameExist) { Plugin.Log.LogWarning("[invite] Open dialogue: currently visiting a trader room"); return false; }
        if (DialogScreenTracker.Open) { Plugin.Log.LogWarning("[invite] Open dialogue: dialogue screen already open"); return false; }
        var profile = app.Session.Profile;
        if (!profile.TradersInfo.ContainsKey(new MongoID(trader))) { Plugin.Log.LogWarning($"[invite] Open dialogue: trader {trader} not in profile"); return false; }
        if (string.IsNullOrEmpty(dialogue)) Plugin.Log.LogWarning($"[invite] Invite for quest {p.QuestId} has no dialogueId, opening the trader's main dialogue");
        try { screen?.Close(); }
        catch (Exception e) { Plugin.Log.LogWarning("[invite] Failed to close chat window: " + e.Message); }
        WhitelistPatch.RegisteredTraders.Add(trader);   // 原生对话屏只认四位商人，其余的靠 WhitelistPatch 放行
        var dc = new ClientDialogController(profile, op.QuestController, op.InventoryController);
        var presenter = new InvitePresenter(dc, trader);
        MongoID? entry = string.IsNullOrEmpty(dialogue) ? null : new MongoID(dialogue);
        new TraderDialogScreen.TraderDialogScreenController(profile, trader, op.QuestController, op.InventoryController, presenter, dc, entry).ShowScreen(EScreenState.Queued);
        Plugin.Instance.StartCoroutine(presenter.Watch());
        return true;
    }
}

/// <summary>顶替原生 TraderAnimationController：每条商人台词的 AnimationData 里有 lipSyncs（lipSyncId + 起止秒）和 subtitles（字幕键 + 起止秒）。
/// 语音按 lipSyncId 找（VoiceCache → 服务端 /files/visitapi/voice/），到点播；字幕发原生 SubtitlesEvent 让对话屏自己的字幕视图显示；
/// 等到最晚的那个结束时刻再放对话继续。玩家点跳过时原生会调 SkipAnimation：停声、清字幕、立刻放行。</summary>
public class InvitePresenter : ITraderAnimationController
{
    readonly ClientDialogController _dc;
    readonly string _trader;
    GameObject _go;
    AudioSource _src;
    Coroutine _co;
    TaskCompletionSource<bool> _tcs;
    bool _disposed;

    public InvitePresenter(ClientDialogController dc, string trader) { _dc = dc; _trader = trader; }

    public NPCObject Animator => null;
    public BaseTraderDialogController DialogController => _dc;

    public Task ExecuteDialogOption(CombinedAnimationData animationData)
    {
        Cancel();
        if (animationData == null || _disposed) return Task.CompletedTask;
        var tcs = new TaskCompletionSource<bool>();
        _tcs = tcs;
        _co = Plugin.Instance.StartCoroutine(Run(animationData, tcs));
        return tcs.Task;
    }

    IEnumerator Run(CombinedAnimationData data, TaskCompletionSource<bool> tcs)
    {
        var lips = data.lipSyncKeysWithParams ?? new List<LipSyncParams>();
        var subs = data.subtitleKeysWithParams ?? new List<SubtitleParams>();
        // 先把这条台词的语音取齐（本地缓存没有就问服务端），单条最多等 8 秒；取不到就只走字幕
        var clips = new List<(LipSyncParams p, AudioClip clip)>();
        foreach (var lp in lips)
        {
            if (lp == null || string.IsNullOrEmpty(lp.Key)) continue;
            AudioClip clip = null; var done = false;
            VoiceCache.Get(_trader, lp.Key, c => { clip = c; done = true; });
            var t0 = Time.unscaledTime;
            while (!done && Time.unscaledTime - t0 < 8f) yield return null;
            if (clip != null) clips.Add((lp, clip));
            else Plugin.Log.LogWarning($"[invite] Voice line {lp.Key} not available, subtitles only");
        }
        if (tcs.Task.IsCompleted) yield break;   // 等语音的时候被跳过了
        var duration = 0f;
        foreach (var lp in lips) if (lp != null) duration = Mathf.Max(duration, lp.End);
        foreach (var s in subs) if (s != null) duration = Mathf.Max(duration, s.End);
        foreach (var (lp, clip) in clips) duration = Mathf.Max(duration, lp.Start + clip.length);
        if (subs.Count > 0) GlobalEventsController.Instance.CreateCommonEvent<SubtitlesEvent>().Invoke(ESubtitlesSource.Common, subs);
        var start = Time.unscaledTime;
        var pending = new List<(LipSyncParams p, AudioClip clip)>(clips);
        var probeAt = -1f;
        while (Time.unscaledTime - start < duration)
        {
            var t = Time.unscaledTime - start;
            for (var i = pending.Count - 1; i >= 0; i--)
                if (t >= pending[i].p.Start) { Play(pending[i].clip, pending[i].p.Volume); pending.RemoveAt(i); probeAt = t + 0.5f; }
            if (probeAt >= 0f && t >= probeAt && _src != null)
            {
                probeAt = -1f;
            }
            yield return null;
        }
        _co = null;
        if (_tcs == tcs) _tcs = null;
        tcs.TrySetResult(true);
    }

    void Play(AudioClip clip, float volume)
    {
        var src = Source();
        src.Stop();
        src.clip = clip;
        src.volume = volume <= 0f ? 1f : Mathf.Clamp01(volume);
        src.Play();
    }

    AudioSource Source()
    {
        if (_src != null) return _src;
        _go = new GameObject("VisitInviteVoice");
        Object.DontDestroyOnLoad(_go);
        _src = _go.AddComponent<AudioSource>();
        _src.spatialBlend = 0f; _src.playOnAwake = false; _src.dopplerLevel = 0f;
        return _src;
    }

    public void SkipAnimation()
    {
        try { GlobalEventsController.Instance.CreateCommonEvent<SubtitlesEndEvent>().Invoke(ESubtitlesSource.Common); }
        catch (Exception e) { Plugin.WarnOnce("invite/clear-subtitles", "[invite] Failed to clear subtitles: " + e.Message); }
        Cancel();
    }

    void Cancel()
    {
        if (_co != null) { Plugin.Instance.StopCoroutine(_co); _co = null; }
        if (_src != null) _src.Stop();
        var tcs = _tcs; _tcs = null;
        tcs?.TrySetResult(true);
    }

    /// 对话屏关了就收尾（对话屏 10 秒内没开起来也收尾）
    public IEnumerator Watch()
    {
        var t0 = Time.unscaledTime;
        while (!DialogScreenTracker.Open && Time.unscaledTime - t0 < 10f) yield return null;
        if (!DialogScreenTracker.Open) Plugin.Log.LogWarning("[invite] Dialogue screen did not open within 10 seconds");
        while (DialogScreenTracker.Open) yield return null;
        Dispose();
    }

    void Dispose()
    {
        _disposed = true;
        Cancel();
        if (_go != null) { Object.Destroy(_go); _go = null; _src = null; }
    }
}

/// <summary>商人台词语音：先看本地缓存 `BepInEx\cache\VisitAPI\voice\&lt;商人&gt;\&lt;lipSyncId&gt;.ogg|.wav`，没有就从服务端
/// `/files/visitapi/voice/&lt;商人&gt;/&lt;lipSyncId&gt;` 拿字节（认头四个字节 OggS / RIFF 定格式）存下来，再用 UnityWebRequest 从文件解成 AudioClip。</summary>
public static class VoiceCache
{
    static readonly Dictionary<string, AudioClip> _clips = new(StringComparer.Ordinal);
    static readonly HashSet<string> _loading = new(StringComparer.Ordinal), _missing = new(StringComparer.Ordinal);

    public static void Get(string trader, string key, Action<AudioClip> done) => Plugin.Instance.StartCoroutine(Run(trader, key, done));

    static IEnumerator Run(string trader, string key, Action<AudioClip> done)
    {
        var id = trader + "/" + key;
        if (_clips.TryGetValue(id, out var cached) && cached != null) { done(cached); yield break; }
        if (_missing.Contains(id)) { done(null); yield break; }
        if (_loading.Contains(id))
        {
            while (_loading.Contains(id)) yield return null;
            _clips.TryGetValue(id, out cached);
            done(cached);
            yield break;
        }
        _loading.Add(id);
        AudioClip clip = null;
        try
        {
            var dir = Path.Combine(BepInEx.Paths.CachePath, "VisitAPI", "voice", trader);
            Directory.CreateDirectory(dir);
            var path = Directory.GetFiles(dir, key + ".*").FirstOrDefault(f => f.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase));
            if (path == null)
            {
                var route = $"/files/visitapi/voice/{trader}/{key}.bin";
                var task = Task.Run(() => RequestHandler.GetData(route));
                while (!task.IsCompleted) yield return null;
                var bytes = task.IsFaulted ? null : task.Result;
                var ext = Sniff(bytes);
                if (ext == null)
                {
                    _missing.Add(id);
                    Plugin.Log.LogWarning($"[invite] Voice {id} not available on server ({(task.IsFaulted ? task.Exception?.GetBaseException().Message : (bytes?.Length ?? 0) + " bytes")})");
                    yield break;
                }
                path = Path.Combine(dir, key + ext);
                File.WriteAllBytes(path, bytes);
            }
            var type = path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? AudioType.WAV : path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ? AudioType.MPEG : AudioType.OGGVORBIS;
            using (var req = UnityWebRequestMultimedia.GetAudioClip("file:///" + path.Replace('\\', '/'), type))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) { _missing.Add(id); Plugin.Log.LogWarning($"[invite] Voice {id} could not be decoded: {req.error}"); yield break; }
                clip = DownloadHandlerAudioClip.GetContent(req);
            }
            // 解出来是空的 / 失败的（坏文件）就把缓存文件删掉，下次重新从服务端拿，别让一个坏文件卡死
            if (clip == null || clip.length <= 0f || clip.loadState == AudioDataLoadState.Failed)
            {
                Plugin.Log.LogWarning($"[invite] Voice {id}: file {Path.GetFileName(path)} did not decode to a usable AudioClip ({(clip == null ? "null" : clip.length.ToString("0.##") + "s " + clip.loadState)}), deleting cache");
                if (clip != null) Object.Destroy(clip);
                clip = null;
                try { File.Delete(path); } catch (Exception e) { Plugin.WarnOnce("invite/bad-cache", "[invite] Failed to delete a bad voice cache file: " + e.Message); }
                _missing.Add(id);
                yield break;
            }
            clip.name = key; _clips[id] = clip;
        }
        finally
        {
            _loading.Remove(id);
            done(clip);
        }
    }

    static string Sniff(byte[] b)
    {
        if (b == null || b.Length < 12) return null;
        if (b[0] == (byte)'O' && b[1] == (byte)'g' && b[2] == (byte)'g' && b[3] == (byte)'S') return ".ogg";
        if (b[0] == (byte)'R' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'F') return ".wav";
        if (b[0] == (byte)'I' && b[1] == (byte)'D' && b[2] == (byte)'3') return ".mp3";          // ID3v2 标签开头
        if (b[0] == 0xFF && (b[1] & 0xE0) == 0xE0) return ".mp3";                               // MPEG 帧同步
        return null;
    }
}
