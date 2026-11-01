using Comfort.Common;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VisitAPI.Native;

/// <summary>把某个场景设成活动场景（灯光、天空盒等 RenderSettings 跟着活动场景走），之后还原成切过去之前的那个。
/// 两边还原的条件不同：SceneLoader 只在活动场景还是自己切过去的那个时才切回（onlyIfStillActive），SceneLighting 收尾时无条件切回。</summary>
sealed class ActiveSceneSwap
{
    Scene _previous, _scene;
    bool _swapped;

    public void Enter(Scene scene)
    {
        var active = SceneManager.GetActiveScene();
        if (active == scene) return;
        _previous = active;
        _scene = scene;
        _swapped = true;
        SceneManager.SetActiveScene(scene);
    }

    public void Restore(bool onlyIfStillActive)
    {
        if (!_swapped) return;
        _swapped = false;
        var previous = _previous; var scene = _scene;
        _previous = default; _scene = default;
        if (previous.IsValid() && previous.isLoaded && (!onlyIfStillActive || SceneManager.GetActiveScene() == scene)) SceneManager.SetActiveScene(previous);
    }
}

public static class SceneLighting
{
    static readonly ActiveSceneSwap _swap = new();

    public static void Apply(Scene scene)
    {
        if (!scene.isLoaded) return;
        foreach (var root in scene.GetRootGameObjects()) SceneShaders.Fix(root);
        _swap.Enter(scene);
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
        _swap.Restore(onlyIfStillActive: false);
    }
}
