using Comfort.Common;
using EFT.CameraControl;
using EFT.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VisitAPI.Native;

/// <summary>
/// 房间就位后的光照接线。**插件不发明任何光照数值**——包里带什么就用什么：
/// 切 active scene 让房间自己的 RenderSettings 生效，再让 1.1 的 `LevelSettings` 应用公共灯光参数。
///
/// 2026-09-05 大清理（坑 #122 之后）：原来这里还有一整套「坏烘焙补偿」（环境光三段色回填 / 拔天空盒 /
/// 关雾 / 按 tarkin 的值改写反射强度）和一个每帧按回去的看门狗。那一套的立论是「包内光照坏了」，
/// 而真正坏掉的是**网格的法线被打包剥光**（#122）——法线补回来之后画面就对了，那套补偿全部作废，
/// 已随 `Narrate.LightingMode` / `Narrate.TarkinEnv` 两个配置一起拆除。旧实现见 Dev_Note #117 / M10。
/// </summary>
public static class SceneLighting
{
    static Scene _previous;
    static bool _active;

    public static void Apply(Scene scene)
    {
        if (!scene.isLoaded) return;
        foreach (var root in scene.GetRootGameObjects()) SceneShaders.Fix(root);
        if (SceneManager.GetActiveScene() != scene)
        {
            _previous = SceneManager.GetActiveScene();
            _active = true;
            SceneManager.SetActiveScene(scene);
        }
        // 切 active scene 会把房间自己的 RenderSettings 覆盖到全局；正式版随后由
        // Vendors_Scripts/LevelSettings 再应用公共灯光参数。
        // LevelSettings 是 MonoBehaviour：`?.` 绕过 Unity 假 null，上一次访问的 Vendors_Scripts 卸掉后若单例没释放，这里就是 MissingReferenceException
        // 把整条 Run 协程打断（09-07 终审）。显式判 Unity 空。
        var level = Singleton<LevelSettings>.Instantiated ? Singleton<LevelSettings>.Instance : null;
        if (level != null) level.ApplySettings();
        else Plugin.Log.LogWarning("[narrate] LevelSettings 单例不在（公共场景没进来或已销毁），公共灯光参数没应用");
        DimReflection();
        // 2026-09-05 拆除 `DeferredShading11`（坑 #126）：把灯光合成换成包内 1.1 的 `Hidden/Internal-DeferredShadingEFT`，
        // 立论出自剥了法线的包上的 G-buffer 对比；实机 A/B 关掉「没啥区别」，连同附加包 `visitapi_deferred11` 一起停用。
        if (scene.name != "Vendors_Scripts") DecalDraw.Schedule();   // 默认关，见坑 #125
        // 坑 #118 作废：房间里的 `Camera_Debug_<商人>` 只是调试标记，不是 1.1 的实际机位——
        // 按它摆相机（横向差 0.55m、俯仰差 3.76°）实机把人物推到画面左边（09-04 SORA「相机错位了」）。
        // 1.1 的机位就是玩家眼睛，维持原样。
        Plugin.Log.LogInfo($"[narrate] lighting armed: active='{SceneManager.GetActiveScene().name}' "
            + $"包内天空盒={(RenderSettings.skybox != null ? RenderSettings.skybox.name : "无")} fog={RenderSettings.fog} "
            + $"环境光={RenderSettings.ambientMode}/{RenderSettings.ambientIntensity:0.##} 反射={RenderSettings.defaultReflectionMode}/{RenderSettings.reflectionIntensity:0.##}");
    }

    /// <summary>环境反射按 0 走、雾关掉（坑 #124，09-05 从「拆除」改回「保留」）。
    /// 1.1 场景 authored 的是 `m_ReflectionIntensity: 1` / `m_Fog: 1`，一度按「不许违背 1.1 原数据」把这段删了，
    /// 实机一对比画面就亮一档、平一档（SORA「还是差一点」）。**原因不在数值，在反射源**：
    /// 房间的 `m_GeneratedSkyboxReflection` 是照着 level642 自己的天空盒 `Default-Skybox`（Skybox/Procedural，
    /// 大白天的蓝空）烤出来的 HDR 立方图（BC6H 128² 8mip，09-05 核过）；而运行时 `LevelSettings` 又把可见天空盒换成
    /// `skybox_night`，没有任何一步重算环境反射。于是强度 1 = 满屋光泽面（房间几乎全是 `p0/Reflective/*`）
    /// 吃满一张白天蓝空的反射。tarkin 的包就是 0，SORA 记得那版「海报和背景颜色很像」——对得上。
    /// 雾同理照关（1.1 的 exp² 密度 0.01 在房间尺度上本就看不出，一起走这个开关）。</summary>
    static void DimReflection()
    {
        if (!Plugin.DimReflection.Value) return;
        var was = $"雾={RenderSettings.fog} 反射强度={RenderSettings.reflectionIntensity:0.##}";
        RenderSettings.fog = false;
        RenderSettings.ambientIntensity = 0f;
        RenderSettings.reflectionIntensity = 0f;
        Plugin.Log.LogInfo($"[narrate] 环境反射压暗: {was} → 雾=关 反射强度=0（坑 #124）");
    }

    public static void Uncover(bool visiting)
    {
        Visibility.Environment(!visiting);
        if (visiting) Visibility.Camera(true);
    }

    public static void Release()
    {
        Uncover(visiting: false);
        if (!_active) return;
        _active = false;
        if (_previous.IsValid() && _previous.isLoaded) SceneManager.SetActiveScene(_previous);
    }
}
