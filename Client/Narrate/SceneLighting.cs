using Comfort.Common;
using EFT.CameraControl;
using EFT.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VisitAPI.Native;

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
        var level = Singleton<LevelSettings>.Instantiated ? Singleton<LevelSettings>.Instance : null;
        if (level != null) level.ApplySettings();
        else Plugin.Log.LogWarning("[narrate] LevelSettings 单例不在（公共场景没进来或已销毁），公共灯光参数没应用");
        DimReflection();
        if (scene.name != "Vendors_Scripts") DecalDraw.Schedule();
        Plugin.Log.LogInfo($"[narrate] lighting armed: active='{SceneManager.GetActiveScene().name}' "
            + $"包内天空盒={(RenderSettings.skybox != null ? RenderSettings.skybox.name : "无")} fog={RenderSettings.fog} "
            + $"环境光={RenderSettings.ambientMode}/{RenderSettings.ambientIntensity:0.##} 反射={RenderSettings.defaultReflectionMode}/{RenderSettings.reflectionIntensity:0.##}");
    }

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
