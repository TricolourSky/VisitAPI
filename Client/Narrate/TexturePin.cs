using System;
using Comfort.Common;
using EFT.Settings;
using UnityEngine;

namespace VisitAPI.Native;

public static class TexturePin
{
    static int _was = -1;

    public static void Apply()
    {
        if (_was >= 0) { Plugin.Log.LogInfo($"[narrate] 贴图 mip 上限: 仍钉着（原值 {_was}），不重复记"); return; }
        _was = QualitySettings.globalTextureMipmapLimit;
        QualitySettings.globalTextureMipmapLimit = 0;
        Plugin.Log.LogInfo($"[narrate] 贴图 mip 上限: {_was} -> 0（{Settings()}）");
    }

    public static void Restore()
    {
        if (_was < 0) return;
        QualitySettings.globalTextureMipmapLimit = _was;
        Plugin.Log.LogInfo($"[narrate] 贴图 mip 上限恢复: {_was}");
        _was = -1;
    }

    static string Settings()
    {
        try
        {
            if (!Singleton<SettingsManager>.Instantiated) return "无设置管理器";
            var g = Singleton<SettingsManager>.Instance.Graphics.Settings;
            return $"画质 TextureQuality={g.TextureQuality.Value} MipStreaming={g.MipStreaming.Value} 流式激活={QualitySettings.streamingMipmapsActive}";
        }
        catch (Exception e) { return "设置读取失败 " + e.GetType().Name; }
    }
}
