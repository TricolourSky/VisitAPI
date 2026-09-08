using UnityEngine;
using UnityEngine.SceneManagement;

namespace VisitAPI.Native;

/// <summary>
/// 09-07 Mechanic 镜片一团黑（材质 `glasses2`：Standard 预乘透明，光滑度 0.91）。五轮实机二分的结论：
///   · 黑的是镜片本身（关掉眼镜渲染器黑块消失，眼窝阴影片无关）；
///   · 网格、贴图、透明混合都正常（换成无光照透明 shader 立刻出色）；
///   · Standard 本身在访问里能点亮（房间椅子换全新 Standard 正常）；
///   · 全新 Standard 预乘透明 + 原贴图：光滑度 0.5 透明清澈、0.7 起整块黑；SORA 判定光滑度 0.5 那档「和正式版一样」。
/// 机理：一块很光滑的透明面颜色几乎全来自环境反射，而访问里透明面拿到的反射源是黑的（坑 #124 把环境反射压成 0、
/// 房间没有反射探针、EFT 自家的全局反射系数也是 0）；把强度打到 1 也没用——强度乘黑还是黑。
/// 处置：访问期把「Standard 预乘透明」材质的光滑度封顶到 0.5。这是对 0.16 访问管线的补偿，不是 1.1 的原数据，
/// 但它是实机验收过的那一档；要彻底还原 1.1（真给角色一份反射源）留待以后有闲再做。
/// </summary>
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
        // 09-07 第二次实机：只把原材质的光滑度压到 0.5 仍是深灰，而实验里「全新 Standard + 只带贴图 + 预乘透明 + 光滑度 0.5」
        // 是清透的（SORA 圈定的那一档）。原材质从 1.1 带来几十项 0.16 的 Standard 不认识或语义不同的属性，逐项对不出是哪一项在压暗，
        // 干脆照验收档位重建：全新材质，只搬贴图、颜色、金属度，光滑度封顶 0.5，其余一律用 shader 默认值。
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
