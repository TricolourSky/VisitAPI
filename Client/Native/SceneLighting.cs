using Comfort.Common;
using EFT.CameraControl;
using EFT.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace VisitAPI.Native;

public static class SceneLighting
{
	private static Scene _previous;

	private static bool _active;

	public static void Apply(Scene scene)
	{
		if (scene.isLoaded)
		{
			GameObject[] rootGameObjects = scene.GetRootGameObjects();
			foreach (GameObject root in rootGameObjects)
			{
				SceneShaders.Fix(root);
			}
			if (SceneManager.GetActiveScene() != scene)
			{
				_previous = SceneManager.GetActiveScene();
				_active = true;
				SceneManager.SetActiveScene(scene);
			}
			if (RenderSettings.ambientLight.maxColorComponent < 0.08f && RenderSettings.ambientSkyColor.maxColorComponent < 0.08f)
			{
				RenderSettings.ambientMode = AmbientMode.Trilight;
				RenderSettings.ambientSkyColor = new Color(0.32f, 0.34f, 0.38f);
				RenderSettings.ambientEquatorColor = new Color(0.24f, 0.24f, 0.26f);
				RenderSettings.ambientGroundColor = new Color(0.14f, 0.13f, 0.12f);
				RenderSettings.ambientIntensity = 1f;
			}
			Shader.SetGlobalFloat("_DirectionLightShadow", 1f);
			Shader.SetGlobalColor("_MinAmbientColor", new Color(0.05f, 0.05f, 0.06f, 1f));
			Plugin.Log.LogDebug($"[narrate] lighting armed: active='{SceneManager.GetActiveScene().name}' ambient={RenderSettings.ambientMode}");
		}
	}

	public static void Uncover(bool visiting)
	{
		EnvironmentUI envUi = Singleton<EnvironmentUI>.Instance;
		if (envUi != null)
		{
			envUi.ShowEnvironment(!visiting);
		}
		CameraManager cameraManager = CameraManager.Instance;
		if (visiting && cameraManager != null && cameraManager.Camera != null && !cameraManager.IsActive)
		{
			cameraManager.IsActive = true;
		}
	}

	public static void Release()
	{
		Uncover(visiting: false);
		if (_active)
		{
			_active = false;
			if (_previous.IsValid() && _previous.isLoaded)
			{
				SceneManager.SetActiveScene(_previous);
			}
		}
	}
}
