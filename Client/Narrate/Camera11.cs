using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityStandardAssets.ImageEffects;

namespace VisitAPI.Native;

/// <summary>
/// 把 0.16 的相机调成 1.1 商人房那台。唯一权威是 1.1 相机预制体 `Cam2_obshaga_zalupa4`
/// （AssetRipper 导出 YAML，62 个组件的开关 + 参数逐字段抄录，09-03 深夜）。两件事，一次做完：
///   ① **名单**：本机（藏身处相机）上有、1.1 没有的后期效果关掉；1.1 关着的同名效果也关。
///   ② **参数**：1.1 开着、本机也有的效果，按预制体的数值灌进去。数值全是 1.1 原数据，插件不发明参数。
/// 相机是每次访问新建、退出即销毁的，这些开关随它一起消失。
///
/// 2026-09-05 合并：原来分在 `CameraRoster.cs`（名单）和 `Camera11.cs`（参数）两个文件，是同一件事，收成一处；
/// 同批拆掉 `BlackGuard` 黑屏看门狗——它是为了查「UltimateBloom 缺 shader 导致整屏黑」写的自动排障器，
/// 凶手早已查明并修好（绕开 SSAAPropagator），留着等于每次访问都白读 20 秒屏幕像素。
/// </summary>
public static class Camera11
{
    // ── 名单：1.1 相机上开着的 ──
    static readonly HashSet<string> On = new(StringComparer.Ordinal)
    {
        "HBAO", "Undithering", "TOD_Scattering", "VolumetricLightRenderer", "NightVision", "ThermalVision",
        "UltimateBloom", "AmbientOcclusion", "PrismEffects", "BloomAndFlares", "CC_Vintage", "DesaturateEffect",
        "ChromaticAberration", "DeathFade", "BloodOnScreen", "PostprocessGrayscale", "DistortCameraFX",
        "EffectsController", "RainScreenDrops", "ScreenWater", "SSAOMask", "DepthOfField", "NightVisionZBlur",
        "VisorEffect", "InfectionEffect", "FaceCoverMaskEffect", "InventoryBlur", "CameraLodBiasController",
        "PerfectCullingCamera", "PerfectCullingCrossSceneSampler", "BreathController", "DistantShadow",
    };

    // ── 名单：1.1 相机上关着的 ──
    static readonly HashSet<string> Off = new(StringComparer.Ordinal)
    {
        "MBOIT_Scattering", "ContactShadows", "Bloom", "Antialiasing", "SceneCameraFollow", "FastBlur",
        "CC_BleachBypass", "CC_ContrastVignette", "CC_DoubleVision", "CC_HueFocus", "CC_RadialBlur", "CC_Sharpen",
        "CC_Technicolor", "CC_BrightnessContrastGamma", "TextureMask", "Tonemapping", "GrenadeFlashScreenEffect",
        "EyeBurn", "HysteresisFilter", "FrostbiteEffect", "TearsEffect", "DigitalGlitch", "GradingPostFX",
    };

    // 1.1 相机上没有、又会改画面、但不是 OnRenderImage 型的 0.16 组件——按名点名。
    // 只关 PostProcessVolume 那类藏身处调色；PostProcessLayer 不能关——它是 0.16 出图链的终点
    //（SSAA 之后的最终 blit 走它），关了整屏没画面只剩菜单背景（09-03 深夜实机，SORA「场景直接没了」）。
    // PostProcessVolume 不在这里关：ReflectionPin 会把它开着、只留 SSR 一项（其余藏身处调色照关）。
    static readonly HashSet<string> Foreign = new(StringComparer.Ordinal) { "HideoutCameraFlashlight" };

    // 0.16 渲染基础设施 + 我们自己的执法器，一律不碰
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
        Fill(camera.GetComponent<BloomAndFlares>(), "BloomAndFlares", BloomAndFlares11);   // 预制体里第一份是开着的，本机两份都关着，用第一份
        // DepthOfField 不灌：0.16 的实现吃同一组参数糊得多（09-03 深夜实机：海报文字/货架/箱子全糊，1.1 里是清楚的）
        // ChromaticAberration 也不灌：景深关掉后背景照样糊，它是仅剩的嫌疑（Shift 0.0015 肉眼本来就看不出，先排除）
        Bloom(camera);
        Fill(camera.GetComponent<HBAO>(), "HBAO", Hbao11);
        Scattering(camera, PrismTransplant.Prefab);
        Desaturate(camera);
        Volumetric(camera);
        // DistantShadow 不装：0.16 里它只画 TOD 太阳的远处阴影（没有 TODSkyProvider 时 Update 直接返回），商人房没有太阳 / 方向光
        Plugin.Log.LogInfo("[narrate] DistantShadow 不装：只画 TOD 太阳的远处阴影，商人房没有太阳");
        ReflectionPin.Apply(camera);   // PPv2 只留 SSR：1.1 场景数据 SSRFactor=1，1.1 的截图是 SSR 开着的样子
        Mask(camera);
    }

    /// <summary>图层遮罩（09-07）：Jaeger 手里那把 MC 255 的全部零件在 1.1 场景里挂在「Weapon Preview」层（19），
    /// 1.1 相机预制体的遮罩 0xFEFFFFDF 只关 UI(5) 和 RainDrops(24)、19 层是开着的；0.16 藏身处相机把 19 层关着
    /// （那是它给库存里武器预览专用的层）→ 手里的枪整把不见。只补这一层，不整份照抄 1.1 的遮罩：其余层的差异先记日志，等实机再定。</summary>
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

    /// <summary>名单那一半：本机相机上不该开的效果逐个关掉，1.1 开着而本机没有的记进日志。</summary>
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

    // 1.1 相机上有、本机也有、参数只有两个数的效果：复古滤镜 CC_Vintage（预制体：filter=10 Jason，amount=0.498）。
    // LUT 由 CC_Vintage 自己按 filter 名从 Resources 里取（"Instagram/Jason"），不用带资源。
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

    /// <summary>1.1 相机上开着的大气散射（Time of Day）。组件优先从包内 1.1 相机预制体照搬（连抖动贴图），预制体没带就抄表。
    /// 0.16 的代码只在本机有初始化好的 TOD_Sky 时才真画，否则原样直通；1.1 的 Prapor / Vendors_Scripts 场景里都没有 TOD_Sky。</summary>
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

    /// <summary>1.1 相机上开着的去饱和 DesaturateEffect：本机相机没有这份组件、包里也没带（SDK 无桩）。类是 0.16 自己的，
    /// shader 按名取本机的，灰度 ramp 是 1.1 原件（256×1，随插件内嵌）。</summary>
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
        // 同 UltimateBloom：运行时追加的效果排在最终出图之后，走 SSAAPropagator 的乒乓缓冲画不到屏幕
        if (!Bypass(target, "DesaturateEffect")) return;
        Fill(target, "DesaturateEffect", Desaturate11);
    }

    /// <summary>1.1 相机上开着的体积光渲染器（本机相机自带、关着）。它只画场上挂了 VolumetricLight 的灯：有就按 1.1 参数打开，
    /// 没有就没东西可画、不开（1.1 的 Prapor 场景里也是 0 盏）。灯只在进场数一次，不进 Update。</summary>
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

    /// <summary>EFT 发布版吞 Debug.LogError，UltimateBloom 缺 shader 时（材质为空 → 合成一步画不出 → 整屏黑）不会留任何痕迹，
    /// 所以加之前自己逐个查；核心四个缺一个就不加，只缺光斑合成版就把各向异性光斑关掉（偏离 1.1 的唯一一处，日志记）。</summary>
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
        Behaviour bloom = camera.GetComponent<UltimateBloom>();
        bloom = Fill(bloom != null ? bloom : (Behaviour)camera.gameObject.AddComponent<UltimateBloom>(), "UltimateBloom", UltimateBloom11);
        if (bloom == null) return;
        if (lacking.Contains(BloomFlareDirt))
        {
            Reflect.Set(bloom, "m_UseAnamorphicFlare", false);
            Plugin.Log.LogWarning("[narrate] 本机没有光斑合成 shader，UltimateBloom 的各向异性光斑关掉（偏离 1.1）");
        }
        // 运行时加的组件排在 SSAAImpl（最终出图）之后：走 SSAAPropagator 的乒乓缓冲会画进一张没人再读的 RT，
        // 屏幕上什么都没有（09-03 深夜两次实机整屏黑、shader 齐全）。绕开它、用普通 Graphics.Blit 直接写到 Unity 给的目标。
        if (!Bypass(bloom, "UltimateBloom")) { bloom.enabled = false; return; }
        Reflect.Set(bloom, "useTriangleBlit", false);
        // 直写后台缓冲时 D3D 的 UV 是倒的（09-03 深夜实机：整屏上下颠倒），UltimateBloom 自带的 m_InvertImage（合成 pass 1）就是给这种情况用的
        Reflect.Set(bloom, "m_InvertImage", true);
        Plugin.Log.LogInfo("[narrate] UltimateBloom 绕开 SSAAPropagator、关 useTriangleBlit、开 m_InvertImage（运行时追加的效果排在最终出图之后，只能这么接）");
    }

    /// 把效果的 `_ssaaPropagator` 置空让它走普通 Blit。`Traverse.Field` 找不到字段时是**无声**的，而不绕开就是整屏黑（09-03 实机）——
    /// 09-07 终审：字段不在就别装这个效果并告警，宁可少一个效果也不要黑屏。
    static bool Bypass(Behaviour target, string name)
    {
        var field = Traverse.Create(target).Field("_ssaaPropagator");
        if (!field.FieldExists())
        {
            Plugin.Log.LogWarning($"[narrate] {name} 没有 _ssaaPropagator 字段（本 build 与 09-03 取证时不同），不装它，免得整屏黑");
            return false;
        }
        field.SetValue(null);
        return true;
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

    // ── 参数表：全部逐字段抄自 1.1 相机预制体 ──

    static readonly Color W = Color.white;

    static readonly (string Field, object Value)[] BloomAndFlares11 =
    {
        ("tweakMode", 1), ("screenBlendMode", 0), ("hdr", 2), ("sepBlurSpread", 4.3f), ("useSrcAlphaAsMask", 0f),
        ("bloomIntensity", 1f), ("bloomThreshold", 0.46f), ("bloomBlurIterations", 3), ("lensflares", false),
        ("hollywoodFlareBlurIterations", 2), ("lensflareMode", 1), ("hollyStretchWidth", 3.5f),
        ("lensflareIntensity", 1f), ("lensflareThreshold", 0.3f),
    };

    // 不灌，但**留着当 1.1 原数据的底档**（这个仓库没有版本控制，抄录一次不易）：色差的三个值。
    // 不灌的理由见 Apply 里那两行注释。
    static readonly (string Field, object Value)[] ChromaticAberration11 =
    {
        ("Shift", 0.0015f), ("Aniso", true), ("Simple", false),
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

    // 1.1 相机上开着、本机相机也有（藏身处参数）的 HBAO：按预制体的三组设置对参数
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

    // 1.1 相机上开着的大气散射（抄表只在包内预制体没带这份组件时用；贴图 bayer_matrix.png 是 1.1 原件）
    static readonly (string Field, object Value)[] TodScattering11 =
    {
        ("Lighten", false), ("FromLevelSettings", true), ("GlobalDensity", 0.004076094f), ("HeightFalloff", 0.029f),
        ("SunrizeGlow", 0.9755f), ("_mboit", false), ("ZeroLevel", 15f),
    };

    // 1.1 相机上开着的去饱和：天气去饱和 0.238，健康/面罩为 0，ramp 偏移 0
    static readonly (string Field, object Value)[] Desaturate11 =
    {
        ("rampOffsetR", 0f), ("rampOffsetG", 0f), ("rampOffsetB", 0f), ("WeatherDesaturate", 0.23796962f), ("HealthDesaturate", 0f),
        ("MaskDesaturate", 0f), ("MinMaxRadius", new Vector2(0.325f, 1f)), ("Radius", 1f), ("RadiusFalloff", 0.425f),
    };

    static readonly (string Field, object Value)[] Volumetric11 = { ("IsOn", true), ("IsOptic", false), ("Resolution", 1) };

    // UltimateBloom 自己按名从 ShadersFinder 取 shader；1.1 那套配置（各向异性光斑开、镜头光斑/尘埃/bokeh 关）要这些：
    static readonly string[] BloomCore = { "Hidden/Ultimate/Sampling", "Hidden/Ultimate/BrightpassMask", "Hidden/Ultimate/BloomMixer", "Hidden/Ultimate/BloomCombine" };
    const string BloomFlareDirt = "Hidden/Ultimate/BloomCombineFlareDirt";   // 开了任一光斑/尘埃时的合成 shader
}
