using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VisitAPI.Native;

/// <summary>1.1 商人房的 38 盏灯**全是「顶点光」**（RenderMode=NotImportant，1.1 场景数据原样如此），
/// 而顶点光在 0.16 的延迟管线里不吃 cookie——1.1 截图上天花板/后墙是形状清楚的光斑，不提就是一片均匀铺开的亮。
/// 强度 / 射程 / 颜色一个不动，只改 renderMode；房间场景退出即卸载，不用还原。配置 `Narrate.PixelLights`。
///
/// 2026-09-05 大清理：删掉了「补偿模式」那条选择性提灯分支——它按射程 ≥4m 挑主灯，还把橙色台灯的强度从 <1 改成 1.0
/// （插件发明参数，违反「不靠调参数伪装」）。那条路随 #122 一起作废。</summary>
public static class SceneRelight
{
    public static void Promote(Scene s)
    {
        if (!s.isLoaded) return;
        if (!Plugin.PixelLights.Value) { Plugin.Log.LogInfo($"[narrate] 灯光提升: 关（'{s.name}' 按包内 ForceVertex 原样）"); return; }
        int promoted = 0, cookies = 0;
        foreach (var l in s.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Light>(true)))
        {
            if (l.renderMode != LightRenderMode.ForceVertex) continue;
            l.renderMode = LightRenderMode.ForcePixel;
            promoted++;
            if (l.cookie != null) cookies++;
        }
        if (promoted > 0)
            Plugin.Log.LogInfo($"[narrate] 灯光提升: '{s.name}' {promoted} 盏顶点光→像素光，其中 {cookies} 盏带 cookie（顶点光不吃 cookie，1.1 的光斑形状就在这）");
    }
}
