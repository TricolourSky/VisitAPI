using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VisitAPI.Native;

public static class SceneRelight
{
    public static void Promote(Scene s)
    {
        if (!s.isLoaded) return;
        foreach (var l in s.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Light>(true)))
            if (l.renderMode == LightRenderMode.ForceVertex) l.renderMode = LightRenderMode.ForcePixel;
    }
}
