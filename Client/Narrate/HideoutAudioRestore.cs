using System;
using Comfort.Common;
using EFT;
using HarmonyLib;

namespace VisitAPI.Native;

[HarmonyPatch(typeof(TarkovApplication.HideoutController), nameof(TarkovApplication.HideoutController.method_10))]
public static class HideoutAudioRestore
{
    static void Postfix()
    {
        try
        {
            if (!BetterAudio.IsInHideout || !MonoBehaviourSingleton<BetterAudio>.Instantiated) return;
            var audio = MonoBehaviourSingleton<BetterAudio>.Instance;
            var key = audio.AudioMixerData?.MainMixerVolume;
            if (string.IsNullOrEmpty(key) || audio.Master == null) { Plugin.Log.LogWarning("[audio] Hideout shown again: master mixer parameter unavailable, left unchanged"); return; }
            if (!audio.Master.GetFloat(key, out var db)) { Plugin.Log.LogWarning("[audio] Hideout shown again: can't read master mixer parameter, left unchanged"); return; }
            if (db >= -0.5f) return;
            audio.FadeMixerVolume(key, 0f, 0.5f, force: true);
        }
        catch (Exception e) { Plugin.Log.LogWarning("[audio] Hideout volume restore failed (hideout left as is): " + e.Message); }
    }
}
