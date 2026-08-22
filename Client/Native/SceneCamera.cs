using Comfort.Common;
using EFT.CameraControl;
using EFT.UI;
using UnityEngine;

namespace VisitAPI.Native;

public static class SceneCamera
{
	private static Camera _cam;

	internal static Camera Current => _cam;

	public static void Show(Transform point)
	{
		if (_cam == null)
		{
			GameObject prefab = Resources.Load<GameObject>("Cam2_fps_hideout");
			if (prefab == null)
			{
				Plugin.Log.LogError("[scene] camera prefab 'Cam2_fps_hideout' missing");
				return;
			}
			GameObject camGo = Object.Instantiate(prefab);
			camGo.name = "VisitSceneCamera";
			_cam = camGo.GetComponent<Camera>();
			if (camGo.GetComponent("CinemachineBrain") is Behaviour brain)
			{
				brain.enabled = false;
			}
			if (camGo.GetComponent("GlobalFog") is Behaviour fog)
			{
				fog.enabled = false;
			}
			_cam.fieldOfView = 60f;
			_cam.clearFlags = CameraClearFlags.Color;
			_cam.backgroundColor = Color.black;
			if (_cam.GetComponent<AudioListener>() == null)
			{
				_cam.gameObject.AddComponent<AudioListener>();
			}
			Object.DontDestroyOnLoad(camGo);
		}
		if (CameraManager.Instance != null && CameraManager.Instance.Camera != null)
		{
			CameraManager.Instance.IsActive = false;
		}
		_cam.gameObject.SetActive(value: true);
		_cam.transform.SetPositionAndRotation(point.position, point.rotation);
		EnvironmentUI instance = Singleton<EnvironmentUI>.Instance;
		if (instance != null)
		{
			instance.ShowEnvironment(value: false);
		}
	}

	public static void Hide()
	{
		if (_cam != null)
		{
			_cam.gameObject.SetActive(value: false);
		}
		if (CameraManager.Instance != null && CameraManager.Instance.Camera != null)
		{
			CameraManager.Instance.IsActive = true;
		}
		EnvironmentUI instance = Singleton<EnvironmentUI>.Instance;
		if (instance != null)
		{
			instance.ShowEnvironment(value: true);
		}
	}
}
