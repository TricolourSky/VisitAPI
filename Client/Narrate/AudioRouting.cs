using System.Linq;
using EFT;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace VisitAPI.Native;

/// <summary>bundle 音源的混音重定向。1.1 场景里的 AudioSource 挂在**随包拷贝**的 1.1 混音台上——
/// EFT 只调自家主混音台的音量参数（ToggleNarrate 写 0dB 的是自家的），拷贝没人管=声音全被吃掉
/// （2026-09-02 大修实证：clip 在播、isPlaying=True、就是没声）。这里把它们改挂到游戏主混音台的同名组，
/// 找不到同名组就挂 Master 根——顺带吃上玩家的音量设置。</summary>
public static class AudioRouting
{
    public static AudioMixerGroup Resolve(AudioMixerGroup group)
    {
        var audio = MonoBehaviourSingleton<BetterAudio>.Instance;
        var master = audio != null ? audio.Master : null;
        if (master == null || group == null || group.audioMixer == master) return group;
        return master.FindMatchingGroups(group.name).FirstOrDefault() ?? audio.MasterMixerGroup;
    }

    public static void Remap(Scene scene)
    {
        if (!scene.isLoaded) return;
        var n = 0;
        var roots = scene.GetRootGameObjects();
        foreach (var src in roots.SelectMany(r => r.GetComponentsInChildren<AudioSource>(true)))
        {
            var target = Resolve(src.outputAudioMixerGroup);
            if (ReferenceEquals(target, src.outputAudioMixerGroup)) continue;
            src.outputAudioMixerGroup = target;
            n++;
        }
        // 环境音组的序列化字段也是死拷贝，且它的 Start/SetMixerGroup 会把死拷贝重新塞回每个播放器——字段必须一起换
        foreach (var g in roots.SelectMany(r => r.GetComponentsInChildren<global::Audio.AmbientSubsystem.AmbientSoundPlayerGroup>(true)))
        {
            var target = Resolve(g._outputMixerGroup);
            if (ReferenceEquals(target, g._outputMixerGroup)) continue;
            g._outputMixerGroup = target;
            g.SetMixerGroup();
            n++;
        }
        if (n > 0) Plugin.Log.LogInfo($"[narrate] 混音重定向: '{scene.name}' {n} 处 → 游戏主混音台");
    }

    /// tarkin 直打包剥掉了 1.1 独有的 NPCSceneAudioController（它唯一的活就是把环境音组打开），
    /// 于是环境音组永远 IsPlaying=False（坑 #101 取证）。场景里没有总控就由我们补开；我们自己的包有总控，这里不插手。
    public static void EnsureAmbient(Scene scene)
    {
        if (!scene.isLoaded) return;
        var roots = scene.GetRootGameObjects();
        if (roots.Any(r => r.GetComponentInChildren<global::NPC.NPCSceneAudioController>(true) != null)) return;
        var started = 0;
        foreach (var g in roots.SelectMany(r => r.GetComponentsInChildren<global::Audio.AmbientSubsystem.AmbientSoundPlayerGroup>(true)))
            if (!g.IsPlaying) { g.Play(); started++; }
        if (started > 0) Plugin.Log.LogInfo($"[narrate] 环境音组补开: '{scene.name}' {started} 组（包里没有场景音总控）");
    }
}
