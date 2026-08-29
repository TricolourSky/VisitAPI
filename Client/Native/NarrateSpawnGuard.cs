using EFT;
using EFT.CameraControl;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VisitAPI.Native;

[HarmonyPatch(typeof(NarrateGame), "Move")]
public static class NarrateSpawnGuard
{
	private static void Prefix(NarrateSceneInfo sceneInfo)
	{
		Transform transform = SceneLoader.FindCameraPoint(SceneManager.GetSceneByName(sceneInfo.sceneName));
		if (transform == null)
		{
			Plugin.Log.LogWarning("[narrate] no camera point in scene - native coordinates in effect");
			return;
		}
		sceneInfo.playerPosition = transform.position - Vector3.up * 1.56f;
		sceneInfo.targetPosition = transform.position + transform.forward * 5f;
		SceneCamera.Show(transform);
		if (SceneCamera.Current != null)
		{
			CameraManager.Instance.Camera = SceneCamera.Current;
		}
		Plugin.Log.LogDebug($"[narrate] scene camera up at {transform.position} (native FPS camera bypassed)");
	}
}
