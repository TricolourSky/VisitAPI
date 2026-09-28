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
        else Plugin.Log.LogWarning("[narrate] LevelSettings singleton missing (shared scene not loaded or already destroyed); shared lighting parameters not applied");
        DimReflection();
    }

    static void DimReflection()
    {
        RenderSettings.fog = false;
        RenderSettings.ambientIntensity = 0f;
        RenderSettings.reflectionIntensity = 0f;
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
