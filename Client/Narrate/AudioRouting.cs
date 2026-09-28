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
        var roots = scene.GetRootGameObjects();
        foreach (var src in roots.SelectMany(r => r.GetComponentsInChildren<AudioSource>(true)))
        {
            var target = Resolve(src.outputAudioMixerGroup);
            if (ReferenceEquals(target, src.outputAudioMixerGroup)) continue;
            src.outputAudioMixerGroup = target;
        }
        foreach (var g in roots.SelectMany(r => r.GetComponentsInChildren<global::Audio.AmbientSubsystem.AmbientSoundPlayerGroup>(true)))
        {
            var target = Resolve(g._outputMixerGroup);
            if (ReferenceEquals(target, g._outputMixerGroup)) continue;
            g._outputMixerGroup = target;
            g.SetMixerGroup();
        }
    }

    public static void EnsureAmbient(Scene scene)
    {
        if (!scene.isLoaded) return;
        var roots = scene.GetRootGameObjects();
        if (roots.Any(r => r.GetComponentInChildren<global::NPC.NPCSceneAudioController>(true) != null)) return;
        foreach (var g in roots.SelectMany(r => r.GetComponentsInChildren<global::Audio.AmbientSubsystem.AmbientSoundPlayerGroup>(true)))
            if (!g.IsPlaying) g.Play();
    }
}
