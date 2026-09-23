using UnityEngine;
using UnityEngine.SceneManagement;

namespace VisitAPI.Native;

public static class GlassProbe
{
    const float MaxSmoothness = 0.5f;

    public static void Report(Scene scene)
    {
        try { Apply(scene); }
        catch (System.Exception e) { Plugin.Log.LogWarning("[narrate] 透明面光滑度封顶出错（已忽略）: " + e.GetType().Name + ": " + e.Message); }
    }

    static void Apply(Scene scene)
    {
        if (!scene.isLoaded) return;
        var rebuilt = new System.Collections.Generic.Dictionary<Material, Material>();
        var touched = new System.Collections.Generic.List<string>();
        var standard = Shader.Find("Standard");
        if (standard == null) return;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials; var changed = false;
                for (var i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || m.shader == null || m.shader.name != "Standard" || !m.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON")) continue;
                    if (!rebuilt.TryGetValue(m, out var fresh))
                    {
                        var g = m.HasProperty("_Glossiness") ? m.GetFloat("_Glossiness") : 0.5f;
                        fresh = new Material(standard) { name = m.name + "_visit" };
                        fresh.SetTexture("_MainTex", m.GetTexture("_MainTex"));
                        fresh.SetTextureScale("_MainTex", m.GetTextureScale("_MainTex"));
                        fresh.SetTextureOffset("_MainTex", m.GetTextureOffset("_MainTex"));
                        fresh.color = m.color;
                        fresh.SetFloat("_Metallic", m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : 0f);
                        fresh.SetFloat("_Glossiness", Mathf.Min(g, MaxSmoothness));
                        fresh.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                        fresh.SetFloat("_Mode", 3f); fresh.SetFloat("_SrcBlend", 1f); fresh.SetFloat("_DstBlend", 10f); fresh.SetFloat("_ZWrite", 0f);
                        fresh.renderQueue = m.renderQueue;
                        rebuilt[m] = fresh;
                        touched.Add($"{m.name}@{r.name}(光滑度 {g:0.##}→{Mathf.Min(g, MaxSmoothness):0.##})");
                    }
                    mats[i] = fresh; changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        if (touched.Count > 0)
            Plugin.Log.LogInfo($"[narrate] 透明面重建: '{scene.name}' {touched.Count} 个 Standard 预乘透明材质按验收档位重建 [{string.Join(", ", touched)}]（访问里透明面的反射源是黑的，越光滑越黑；09-07 实机定 0.5）");
    }
}
