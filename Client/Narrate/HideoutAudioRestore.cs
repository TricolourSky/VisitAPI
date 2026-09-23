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
            if (string.IsNullOrEmpty(key) || audio.Master == null) { Plugin.Log.LogWarning("[audio] 藏身处重新显示：拿不到主混音参数，没动"); return; }
            if (!audio.Master.GetFloat(key, out var db)) { Plugin.Log.LogWarning("[audio] 藏身处重新显示：主混音参数读不到，没动"); return; }
            if (db >= -0.5f) { Plugin.Log.LogInfo($"[audio] 藏身处重新显示：主混音 {db:0.#} dB，正常"); return; }
            audio.FadeMixerVolume(key, 0f, 0.5f, force: true);
            Plugin.Log.LogInfo($"[audio] 藏身处重新显示：主混音 {db:0.#} dB（访问退出时被压到静音）→ 淡回 0 dB");
        }
        catch (Exception e) { Plugin.Log.LogWarning("[audio] 藏身处音量恢复失败（藏身处照原样）: " + e.Message); }
    }
}
