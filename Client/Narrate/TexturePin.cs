using System;
using Comfort.Common;
using EFT.Settings;
using UnityEngine;

namespace VisitAPI.Native;

/// <summary>
/// 海报印刷 / 箱子喷字 / 远处贴图整体变淡变糊的根（09-04 凌晨，SORA 框图 + 反编译核对）：
/// 0.16 在画质「MipStreaming」关着时把「贴图质量」直接写成 `QualitySettings.globalTextureMipmapLimit`（高=0 / 中=1 / 低=2），
/// 全局砍掉最高几级 mip。EFT 自己的贴图走流式无所谓，包里的贴图不是流式的，被硬砍到 1/2 或 1/4 分辨率——
/// 印刷和喷字先糊，颜色平均后就发灰发白；更要命的是贴花渲染器 `StaticDeferredDecalDrawInstance.CreateTextureArray`
/// 在上限≠0 时逐级 `CopyTexture` 的尺寸对不上（源 mip i 拷进小一号数组的 mip i），拷贝静默失败，贴花只剩个影子
/// （海报印刷、箱子喷字、墙上的漆字全是贴花）。1.1 的截图是满贴图质量。
/// 访问期把上限钉成 0（进场、场景加载前，贴花数组是在加载时建的），退出恢复玩家原值。
/// </summary>
public static class TexturePin
{
    static int _was = -1;

    public static void Apply()
    {
        // 幂等：上一次访问半路失败没走到 Restore 时 _was 还记着玩家原值，这里再记一遍就会把「0」当成原值，退出时把玩家画质永久改成 0（09-07 终审）
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
