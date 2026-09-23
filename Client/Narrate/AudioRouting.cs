using System.Linq;
using EFT;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace VisitAPI.Native;

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
