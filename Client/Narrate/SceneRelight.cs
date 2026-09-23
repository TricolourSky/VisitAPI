using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VisitAPI.Native;

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
