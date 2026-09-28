using System;
using System.IO;
using System.Linq;
using System.Text;
using EFT.CameraControl;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace VisitAPI.Native;

/// <summary>09-26 为对 1.1 的访问房间画面写：把当前访问房间的渲染状态（全局光照 / 雾、每盏灯、反射探针、粒子、烘焙光照、相机效果）
/// 导成文本，格式和 1.1MCP 那边的同名查询一致，两边逐行比。控制台 visit_scenedump。只读，不改任何东西</summary>
public static class SceneDump
{
    static string V(Vector3 v) => $"({v.x:0.###},{v.y:0.###},{v.z:0.###})";
    static string C(Color c) => $"#{(int)Math.Round(Mathf.Clamp01(c.r) * 255):X2}{(int)Math.Round(Mathf.Clamp01(c.g) * 255):X2}{(int)Math.Round(Mathf.Clamp01(c.b) * 255):X2}{(c.a < 0.999f ? $"@{c.a:0.##}" : "")}";

    static string PathOf(Transform t)
    {
        var s = t.name;
        for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
        return s;
    }

    public static string Write()
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# 0.16 scene dump {DateTime.Now:yyyy-MM-dd HH:mm:ss} screen {Screen.width}x{Screen.height}");
            sb.AppendLine($"RenderSettings fog={RenderSettings.fog} mode={RenderSettings.fogMode} color={C(RenderSettings.fogColor)} density={RenderSettings.fogDensity:0.#####} start={RenderSettings.fogStartDistance:0.##} end={RenderSettings.fogEndDistance:0.##}");
            sb.AppendLine($"  ambient mode={RenderSettings.ambientMode} sky={C(RenderSettings.ambientSkyColor)} equator={C(RenderSettings.ambientEquatorColor)} ground={C(RenderSettings.ambientGroundColor)} intensity={RenderSettings.ambientIntensity:0.###} reflection={RenderSettings.reflectionIntensity:0.###} bounces={RenderSettings.reflectionBounces} defaultReflection={RenderSettings.defaultReflectionMode} skybox={(RenderSettings.skybox == null ? "null" : RenderSettings.skybox.name)} sun={(RenderSettings.sun == null ? "null" : RenderSettings.sun.name)}");
            sb.AppendLine($"QualitySettings pixelLights={QualitySettings.pixelLightCount} shadows={QualitySettings.shadows} shadowDist={QualitySettings.shadowDistance:0.#} shadowCascades={QualitySettings.shadowCascades} lodBias={QualitySettings.lodBias:0.##} aniso={QualitySettings.anisotropicFiltering}");
            sb.AppendLine($"Lightmaps count={LightmapSettings.lightmaps?.Length ?? 0} mode={LightmapSettings.lightmapsMode} probes={(LightmapSettings.lightProbes == null ? 0 : LightmapSettings.lightProbes.count)}");
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || !scene.name.StartsWith("Vendors", StringComparison.OrdinalIgnoreCase)) continue;
                var roots = scene.GetRootGameObjects();
                sb.AppendLine($"== scene {scene.name}");
                foreach (var l in roots.SelectMany(r => r.GetComponentsInChildren<Light>(true)).OrderBy(x => PathOf(x.transform)))
                {
                    var bo = l.bakingOutput;
                    sb.AppendLine($"  light {PathOf(l.transform)} on={l.enabled && l.gameObject.activeInHierarchy} {l.type} render={l.renderMode} color={C(l.color)} intensity={l.intensity:0.###} range={l.range:0.##} spot={l.spotAngle:0.#} shadows={l.shadows}/{l.shadowStrength:0.##} cookie={(l.cookie == null ? "-" : l.cookie.name)} mask=0x{l.cullingMask:X8} bounce={l.bounceIntensity:0.##} baked={bo.isBaked}/{bo.lightmapBakeType}/{bo.mixedLightingMode} pos={V(l.transform.position)}");
                }
                foreach (var p in roots.SelectMany(r => r.GetComponentsInChildren<ReflectionProbe>(true)).OrderBy(x => PathOf(x.transform)))
                    sb.AppendLine($"  probe {PathOf(p.transform)} on={p.enabled && p.gameObject.activeInHierarchy} mode={p.mode} intensity={p.intensity:0.###} importance={p.importance} box={p.boxProjection} size={V(p.size)} baked={(p.bakedTexture == null ? "-" : p.bakedTexture.name)} custom={(p.customBakedTexture == null ? "-" : p.customBakedTexture.name)}");
                foreach (var ps in roots.SelectMany(r => r.GetComponentsInChildren<ParticleSystem>(true)).OrderBy(x => PathOf(x.transform)))
                {
                    var pr = ps.GetComponent<ParticleSystemRenderer>();
                    var mat = pr != null ? pr.sharedMaterial : null;
                    sb.AppendLine($"  particles {PathOf(ps.transform)} active={ps.gameObject.activeInHierarchy} playing={ps.isPlaying} alive={ps.particleCount} max={ps.main.maxParticles} renderer={(pr != null && pr.enabled)} material={(mat == null ? "-" : mat.name)} shader={(mat == null || mat.shader == null ? "-" : mat.shader.name)}");
                }
                var renderers = roots.SelectMany(r => r.GetComponentsInChildren<Renderer>(true)).ToList();
                sb.AppendLine($"  renderers={renderers.Count} lightmapped={renderers.Count(r => r.lightmapIndex >= 0 && r.lightmapIndex < 65534)} active={renderers.Count(r => r.enabled && r.gameObject.activeInHierarchy)}");
            }
            var cam = CameraManager.Instance != null ? CameraManager.Instance.Camera : null;
            if (cam != null)
            {
                sb.AppendLine($"CAMERA {PathOf(cam.transform)} pos={V(cam.transform.position)} rot={V(cam.transform.eulerAngles)} fov={cam.fieldOfView:0.##} hdr={cam.allowHDR} path={cam.actualRenderingPath} mask=0x{cam.cullingMask:X8}");
                foreach (var c in cam.GetComponents<Component>())
                {
                    if (c == null || c is Transform) continue;
                    var off = c is Behaviour b && !b.enabled ? " [off]" : "";
                    sb.AppendLine($"   {c.GetType().Name}{off}");
                }
            }
            Directory.CreateDirectory(UiDump.Dir);
            var file = Path.Combine(UiDump.Dir, $"scene016-{DateTime.Now:MMdd-HHmmss}.txt");
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
            return file;
        }
        catch (Exception e) { Plugin.Log.LogWarning("[scenedump] failed: " + e.Message); return null; }
    }
}
