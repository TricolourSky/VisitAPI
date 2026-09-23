using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityStandardAssets.ImageEffects;

namespace VisitAPI.Native;

public static class Camera11
{
    static readonly HashSet<string> On = new(StringComparer.Ordinal)
    {
        "HBAO", "Undithering", "TOD_Scattering", "VolumetricLightRenderer", "NightVision", "ThermalVision",
        "UltimateBloom", "AmbientOcclusion", "PrismEffects", "BloomAndFlares", "CC_Vintage", "DesaturateEffect",
        "ChromaticAberration", "DeathFade", "BloodOnScreen", "PostprocessGrayscale", "DistortCameraFX",
        "EffectsController", "RainScreenDrops", "ScreenWater", "SSAOMask", "DepthOfField", "NightVisionZBlur",
        "VisorEffect", "InfectionEffect", "FaceCoverMaskEffect", "InventoryBlur", "CameraLodBiasController",
        "PerfectCullingCamera", "PerfectCullingCrossSceneSampler", "BreathController", "DistantShadow",
    };

    static readonly HashSet<string> Off = new(StringComparer.Ordinal)
    {
        "MBOIT_Scattering", "ContactShadows", "Bloom", "Antialiasing", "SceneCameraFollow", "FastBlur",
        "CC_BleachBypass", "CC_ContrastVignette", "CC_DoubleVision", "CC_HueFocus", "CC_RadialBlur", "CC_Sharpen",
        "CC_Technicolor", "CC_BrightnessContrastGamma", "TextureMask", "Tonemapping", "GrenadeFlashScreenEffect",
        "EyeBurn", "HysteresisFilter", "FrostbiteEffect", "TearsEffect", "DigitalGlitch", "GradingPostFX",
    };

    static readonly HashSet<string> Foreign = new(StringComparer.Ordinal) { "HideoutCameraFlashlight" };

    static readonly HashSet<string> Keep = new(StringComparer.Ordinal)
    {
        "Camera", "AudioListener", "CinemachineBrain", "SSAA", "SSAAImpl", "SSAAPropagator", "SSAAPropagatorOpaque",
        "StreamingController", "AreaLightManager", "PostProcessLayer", "NarrateFovEnforcer",
    };

    public static void Apply(Camera camera)
    {
        if (camera == null) return;
        Roster(camera);
        Vintage(camera);
        Fill(camera.GetComponent<BloomAndFlares>(), "BloomAndFlares", BloomAndFlares11);
        Bloom(camera);
        Fill(camera.GetComponent<HBAO>(), "HBAO", Hbao11);
        Scattering(camera, PrismTransplant.Prefab);
        Desaturate(camera);
        Volumetric(camera);
        Plugin.Log.LogInfo("[narrate] DistantShadow 不装：只画 TOD 太阳的远处阴影，商人房没有太阳");
        ReflectionPin.Apply(camera);
        Mask(camera);
    }

    const int Mask11 = unchecked((int)0xFEFFFFDF);
    static void Mask(Camera camera)
    {
        var preview = LayerMask.NameToLayer("Weapon Preview");
        if (preview < 0) { Plugin.Log.LogWarning("[narrate] 本机没有 Weapon Preview 层，枪械零件的图层遮罩没法补"); return; }
        var was = camera.cullingMask;
        camera.cullingMask |= 1 << preview;
        var diff = new List<string>();
        for (var i = 0; i < 32; i++)
            if (((camera.cullingMask >> i) & 1) != ((Mask11 >> i) & 1))
                diff.Add($"{i}:{LayerMask.LayerToName(i)}={(((camera.cullingMask >> i) & 1) != 0 ? "开" : "关")}(1.1={(((Mask11 >> i) & 1) != 0 ? "开" : "关")})");
        Plugin.Log.LogInfo($"[narrate] 相机图层遮罩: 0x{was:X8} → 0x{camera.cullingMask:X8}（补 Weapon Preview 层 {preview}，1.1 相机 0x{Mask11:X8}）；与 1.1 仍不同的层 {diff.Count} 个 [{string.Join(", ", diff)}]");
    }

    static void Roster(Camera camera)
    {
        var disabled = new List<string>();
        var state = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var b in camera.GetComponents<Behaviour>())
        {
            if (b == null) continue;
            var name = b.GetType().Name;
            if (!state.ContainsKey(name) || b.enabled) state[name] = b.enabled;
            if (!ShouldDisable(name, b)) continue;
            b.enabled = false;
            disabled.Add(name);
        }
        Plugin.Log.LogInfo($"[narrate] 相机按 1.1 名单关掉 {disabled.Count} 个效果: {string.Join(", ", disabled)}");
        var wanted = On.Where(n => !state.TryGetValue(n, out var on) || !on)
                       .Select(n => state.ContainsKey(n) ? n + "(关)" : n + "(缺)");
        Plugin.Log.LogInfo($"[narrate] 1.1 开着而本机相机没有/没开（待打包侧补桩后搬）: {string.Join(", ", wanted)}");
    }

    static bool ShouldDisable(string name, Behaviour b)
    {
        if (!b.enabled || Keep.Contains(name) || On.Contains(name)) return false;
        if (Off.Contains(name) || Foreign.Contains(name)) return true;
        return IsImageEffect(b.GetType());
    }

    static bool IsImageEffect(Type type)
    {
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (var t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            if (t.GetMethod("OnRenderImage", any) != null) return true;
        return false;
    }

    static void Vintage(Camera camera)
    {
        var vintage = camera.GetComponent<CC_Vintage>();
        if (vintage == null) { Plugin.Log.LogWarning("[narrate] 本机相机没有 CC_Vintage，1.1 的复古滤镜没法对上"); return; }
        var before = $"{vintage.filter}/{vintage.amount:0.###}/{(vintage.enabled ? "开" : "关")}";
        vintage.filter = CC_Vintage.Filter.Jason;
        vintage.amount = 0.498f;
        vintage.enabled = true;
        Plugin.Log.LogInfo($"[narrate] CC_Vintage 按 1.1 相机预制体: {before} -> Jason/0.498/开");
    }

    static void Scattering(Camera camera, Component prefab)
    {
        var target = camera.GetComponent<TOD_Scattering>() ?? camera.gameObject.AddComponent<TOD_Scattering>();
        var from = prefab != null ? prefab.GetComponent<TOD_Scattering>() : null;
        string source;
        if (from != null)
        {
            source = $"包内相机预制体照搬 {Reflect.Copy(from, target, typeof(TOD_Scattering), out _)} 项";
        }
        else
        {
            Fill(target, "TOD_Scattering", TodScattering11);
            target.DitheringTexture = VisitArt.LoadTexture("bayer_matrix.png", TextureWrapMode.Repeat, FilterMode.Point, linear: true);
            source = "包内预制体没带这份组件，抄表";
        }
        target.enabled = true;
        var sky = MonoBehaviourSingleton<TOD_Sky>.Instance;
        var state = sky != null && sky.Initialized ? "已初始化，散射会画" : "无 / 未初始化，散射直通不画";
        Plugin.Log.LogInfo($"[narrate] TOD_Scattering 按 1.1 装上（{source}，密度 {target.GlobalDensity:0.####}）；本机 TOD_Sky {state}");
    }

    static void Desaturate(Camera camera)
    {
        var shader = ShadersFinder.Find("Hidden/Desaturate Effect");
        if (shader == null || !shader.isSupported)
        {
            Plugin.Log.LogWarning("[narrate] 本机没有 'Hidden/Desaturate Effect' shader，1.1 的去饱和装不上");
            return;
        }
        var target = camera.GetComponent<DesaturateEffect>() ?? camera.gameObject.AddComponent<DesaturateEffect>();
        Reflect.Set(target, "shader", shader);
        target.textureRamp = VisitArt.LoadTexture("grayscale_ramp.png", TextureWrapMode.Clamp, FilterMode.Bilinear);
        Fill(target, "DesaturateEffect", Desaturate11);
        PostChain.Register(target, "DesaturateEffect", target.OnRenderImage);
    }

    static void Volumetric(Camera camera)
    {
        var renderer = camera.GetComponent<VolumetricLightRenderer>();
        var lights = UnityEngine.Object.FindObjectsByType<VolumetricLight>(FindObjectsSortMode.None).Length;
        if (renderer == null || lights == 0)
        {
            Plugin.Log.LogInfo($"[narrate] VolumetricLightRenderer 不开：本机相机{(renderer != null ? "有" : "没有")}这份组件，场上带 VolumetricLight 的灯 {lights} 盏");
            return;
        }
        Fill(renderer, "VolumetricLightRenderer", Volumetric11);
        Plugin.Log.LogInfo($"[narrate] VolumetricLightRenderer 按 1.1 打开，场上带 VolumetricLight 的灯 {lights} 盏");
    }

    static void Bloom(Camera camera)
    {
        var lacking = BloomCore.Append(BloomFlareDirt)
            .Where(n => { var s = ShadersFinder.Find(n); return s == null || !s.isSupported; }).ToList();
        if (lacking.Count > 0) Plugin.Log.LogWarning($"[narrate] UltimateBloom 本机缺/不支持的 shader: {string.Join(", ", lacking)}");
        if (lacking.Any(BloomCore.Contains))
        {
            Plugin.Log.LogWarning("[narrate] UltimateBloom 核心 shader 不齐，1.1 的柔光这条路走不通，不加");
            return;
        }
        var bloom = camera.GetComponent<UltimateBloom>() ?? camera.gameObject.AddComponent<UltimateBloom>();
        if (Fill(bloom, "UltimateBloom", UltimateBloom11) == null) return;
        if (lacking.Contains(BloomFlareDirt))
        {
            Reflect.Set(bloom, "m_UseAnamorphicFlare", false);
            Plugin.Log.LogWarning("[narrate] 本机没有光斑合成 shader，UltimateBloom 的各向异性光斑关掉（偏离 1.1）");
        }
        PostChain.Register(bloom, "UltimateBloom", bloom.OnRenderImage);
    }

    static Behaviour Fill(Behaviour target, string name, (string Field, object Value)[] table)
    {
        if (target == null)
        {
            Plugin.Log.LogWarning($"[narrate] 本机相机没有 {name}，1.1 的这份参数没处放");
            return null;
        }
        var missing = new List<string>();
        foreach (var (field, value) in table)
            if (!Reflect.Set(target, field, value)) missing.Add(field);
        target.enabled = true;
        Plugin.Log.LogInfo($"[narrate] {name} 按 1.1 相机预制体灌 {table.Length - missing.Count}/{table.Length} 项并打开"
            + (missing.Count > 0 ? $"，本机没有的字段: {string.Join(", ", missing)}" : ""));
        return target;
    }

    static readonly Color W = Color.white;

    static readonly (string Field, object Value)[] BloomAndFlares11 =
    {
        ("tweakMode", 1), ("screenBlendMode", 0), ("hdr", 2), ("sepBlurSpread", 4.3f), ("useSrcAlphaAsMask", 0f),
        ("bloomIntensity", 1f), ("bloomThreshold", 0.46f), ("bloomBlurIterations", 3), ("lensflares", false),
        ("hollywoodFlareBlurIterations", 2), ("lensflareMode", 1), ("hollyStretchWidth", 3.5f),
        ("lensflareIntensity", 1f), ("lensflareThreshold", 0.3f),
    };

    static readonly (string Field, object Value)[] UltimateBloom11 =
    {
        ("m_SamplingMinHeight", 400f), ("m_ResSamplingPixelCount", new[] { 400f, 577.7778f, 755.55554f, 933.3333f, 1111.1111f, 1288.8888f }),
        ("m_SamplingMode", 0), ("m_BlendMode", 0), ("m_ScreenMaxIntensity", 0f), ("m_QualityPreset", 0), ("m_HDR", 1), ("m_ScreenBlendMode", 1),
        ("m_BloomIntensity", 0.34605542f), ("m_BloomThreshhold", 2.1f), ("m_BloomThreshholdColor", W), ("m_DownscaleCount", 5), ("m_IntensityManagement", 1),
        ("m_BloomIntensities", new[] { 1f, 0f, 5f, 2.7952757f, 5f, 1f, 1f, 1f }),
        ("m_BloomColors", new[] { W, W, W, W, new Color(1f, 0.9413793f, 0.75f, 1f), W, W, W }),
        ("m_BloomUsages", new[] { false, false, true, true, true, true, true, true }),
        ("m_BloomCurve.m_BlackPoint", 0f), ("m_BloomCurve.m_WhitePoint", 1f), ("m_BloomCurve.m_CrossOverPoint", 0.2896972f),
        ("m_BloomCurve.m_ToeStrength", 0.99f), ("m_BloomCurve.m_ShoulderStrength", 0.15056132f), ("m_BloomCurve.m_Highlights", 0f),
        ("m_BloomCurve.m_k", 0.0047784615f), ("m_BloomCurve.m_ToeCoef", new Vector4(4.778457E-05f, -0.99f, 0f, 0.2896972f)),
        ("m_BloomCurve.m_ShoulderCoef", new Vector4(0.99522156f, 0.15056132f, -0.28831288f, 0.5597415f)),
        ("useTriangleBlit", true), ("m_UseLensFlare", false), ("m_FlareTreshold", 0.8f), ("m_FlareIntensity", 0.25f), ("m_FlareGlobalScale", 1f),
        ("m_FlareScales", new Vector4(1f, 0.6f, 0.5f, 0.4f)), ("m_FlareScalesNear", new Vector4(1f, 0.8f, 0.6f, 0.5f)),
        ("m_FlareRendering", 1), ("m_FlareType", 1), ("m_FlareBlurQuality", 2), ("m_UseBokehFlare", false), ("m_BokehScale", 0.4f), ("m_BokehFlareQuality", 1),
        ("m_UseAnamorphicFlare", true), ("m_AnamorphicFlareTreshold", 0.8f), ("m_AnamorphicFlareIntensity", 0.8f),
        ("m_AnamorphicDownscaleCount", 4), ("m_AnamorphicBlurPass", 2),
        ("m_AnamorphicBloomIntensities", new[] { 0.53921574f, 0f, 0f, 1.4435697f, 1f, 1f, 1f, 1f }),
        ("m_AnamorphicBloomColors", new[] { W, W, W, W, W, W, W, W }),
        ("m_AnamorphicBloomUsages", new[] { false, false, false, true, true, true, true, true }),
        ("m_AnamorphicSmallVerticalBlur", false), ("m_AnamorphicDirection", 0), ("m_AnamorphicScale", 1.5f),
        ("m_UseStarFlare", false), ("m_StarFlareTreshol", 0.8f), ("m_StarFlareIntensity", 2.118863f), ("m_StarScale", 2.1162791f),
        ("m_StarDownscaleCount", 6), ("m_StarBlurPass", 1),
        ("m_StarBloomIntensities", new[] { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f }), ("m_StarBloomColors", new[] { W, W, W, W, W, W, W, W }),
        ("m_StarBloomUsages", new[] { true, true, true, true, true, true, true, true }),
        ("m_UseLensDust", false), ("m_DustIntensity", 2.3674912f), ("m_DirtLightIntensity", 4.1342754f),
        ("m_DownsamplingQuality", 0), ("m_UpsamplingQuality", 0), ("m_TemporalStableDownsampling", false), ("m_InvertImage", false),
        ("m_DirectDownSample", false), ("m_DirectUpsample", true),
    };

    static readonly (string Field, object Value)[] Hbao11 =
    {
        ("m_Presets.preset", 2), ("m_GeneralSettings.integrationStage", 1), ("m_GeneralSettings.quality", 2),
        ("m_GeneralSettings.deinterleaving", 0), ("m_GeneralSettings.resolution", 0), ("m_GeneralSettings.noiseType", 1), ("m_GeneralSettings.displayMode", 0),
        ("m_AOSettings.radius", 1.46f), ("m_AOSettings.maxRadiusPixels", 128f), ("m_AOSettings.bias", 0.05f), ("m_AOSettings.intensity", 0.75f),
        ("m_AOSettings.useMultiBounce", false), ("m_AOSettings.multiBounceInfluence", 1f), ("m_AOSettings.offscreenSamplesContribution", 0f),
        ("m_AOSettings.maxDistance", 150f), ("m_AOSettings.distanceFalloff", 50f), ("m_AOSettings.perPixelNormals", 0), ("m_AOSettings.baseColor", Color.black),
        ("m_ColorBleedingSettings.enabled", false), ("m_ColorBleedingSettings.saturation", 0.75f), ("m_ColorBleedingSettings.albedoMultiplier", 3.2f),
        ("m_ColorBleedingSettings.brightnessMask", 1f), ("m_ColorBleedingSettings.brightnessMaskRange", new Vector2(0.8f, 1.2f)),
        ("m_BlurSettings.amount", 2), ("m_BlurSettings.sharpness", 8f), ("m_BlurSettings.downsample", false), ("useTriangleBlit", true),
    };

    static readonly (string Field, object Value)[] TodScattering11 =
    {
        ("Lighten", false), ("FromLevelSettings", true), ("GlobalDensity", 0.004076094f), ("HeightFalloff", 0.029f),
        ("SunrizeGlow", 0.9755f), ("_mboit", false), ("ZeroLevel", 15f),
    };

    static readonly (string Field, object Value)[] Desaturate11 =
    {
        ("rampOffsetR", 0f), ("rampOffsetG", 0f), ("rampOffsetB", 0f), ("WeatherDesaturate", 0.23796962f), ("HealthDesaturate", 0f),
        ("MaskDesaturate", 0f), ("MinMaxRadius", new Vector2(0.325f, 1f)), ("Radius", 1f), ("RadiusFalloff", 0.425f),
    };

    static readonly (string Field, object Value)[] Volumetric11 = { ("IsOn", true), ("IsOptic", false), ("Resolution", 1) };

    static readonly string[] BloomCore = { "Hidden/Ultimate/Sampling", "Hidden/Ultimate/BrightpassMask", "Hidden/Ultimate/BloomMixer", "Hidden/Ultimate/BloomCombine" };
    const string BloomFlareDirt = "Hidden/Ultimate/BloomCombineFlareDirt";
}
