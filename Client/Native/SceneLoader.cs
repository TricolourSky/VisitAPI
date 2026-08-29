using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using EFT;
using EFT.Dialogs;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VisitAPI.Native;

public static class SceneLoader
{
	private static readonly Dictionary<string, AssetBundle> _bundles = new Dictionary<string, AssetBundle>(StringComparer.OrdinalIgnoreCase);

	private static Type _traderScene;

	private static PropertyInfo _camPoint;

	private static PropertyInfo _traderGo;

	private static string _sceneName;

	private static bool _busy;

	public static bool IsOpen => _sceneName != null;

	public static bool Requested { get; private set; }

	private static string Root => Path.Combine(BepInEx.Paths.PluginPath, "VisitAPI", "scenes");

	public static void Open(string traderId, ClientDialogController dc)
	{
		if (!_busy && !IsOpen)
		{
			Requested = true;
			Plugin.Instance.StartCoroutine(OpenRoutine(traderId, dc));
		}
	}

	public static void Close()
	{
		Requested = false;
		if (!_busy && IsOpen)
		{
			Plugin.Instance.StartCoroutine(CloseRoutine());
		}
	}

	private static IEnumerator OpenRoutine(string traderId, ClientDialogController dc)
	{
		_busy = true;
		AssetBundle bundle = EnsureNarrateBundles(traderId);
		if (bundle == null)
		{
			Plugin.Log.LogWarning("[scene] no room bundle for " + traderId);
		}
		else
		{
			string name = Path.GetFileNameWithoutExtension(bundle.GetAllScenePaths()[0]);
			if (!SceneManager.GetSceneByName(name).isLoaded)
			{
				AsyncOperation op = SceneManager.LoadSceneAsync(name, LoadSceneMode.Additive);
				while (!op.isDone)
				{
					yield return null;
				}
			}
			Scene scene = SceneManager.GetSceneByName(name);
			if (!Requested)
			{
				if (scene.isLoaded)
				{
					AsyncOperation un = SceneManager.UnloadSceneAsync(scene);
					while (un != null && !un.isDone)
					{
						yield return null;
					}
				}
				Plugin.Log.LogDebug("[scene] '" + name + "' discarded - dialog closed during load");
				_busy = false;
				yield break;
			}
			GameObject[] roots = scene.GetRootGameObjects();
			foreach (GameObject root in roots)
			{
				SceneShaders.Fix(root);
				// 抬离菜单/藏身处几何避免穿插
				root.transform.position += new Vector3(0f, 300f, 0f);
			}
			SceneManager.SetActiveScene(scene);
			_sceneName = name;
			Transform cam = CameraPoint(roots);
			if (cam != null)
			{
				SceneCamera.Show(cam);
			}
			else
			{
				Plugin.Log.LogWarning("[scene] no camera point in " + name);
			}
			Animate(roots, dc);
			Plugin.Log.LogDebug("[scene] '" + name + "' staged for " + traderId);
		}
		_busy = false;
	}

	private static IEnumerator CloseRoutine()
	{
		_busy = true;
		Scene scene = SceneManager.GetSceneByName(_sceneName);
		_sceneName = null;
		if (scene.isLoaded)
		{
			AsyncOperation op = SceneManager.UnloadSceneAsync(scene);
			while (op != null && !op.isDone)
			{
				yield return null;
			}
		}
		SceneCamera.Hide();
		_busy = false;
	}

	internal static bool HasRoom(string traderId)
	{
		return RoomFile(traderId) != null;
	}

	internal static AssetBundle EnsureNarrateBundles(string traderId)
	{
		if (!Bind() || Bundle("vendors_shared") == null)
		{
			return null;
		}
		string roomFile = RoomFile(traderId);
		return (roomFile != null) ? Bundle(roomFile) : null;
	}

	private static string RoomFile(string traderId)
	{
		string path = Path.Combine(Root, "bundles", "vendors");
		return Directory.Exists(path) ? Directory.GetFiles(path, traderId + "*").Select(Path.GetFileName).FirstOrDefault() : null;
	}

	private static bool Bind()
	{
		if (_traderScene != null)
		{
			return true;
		}
		string dllPath = Path.Combine(Root, "tradermod.shared.dll");
		if (!File.Exists(dllPath))
		{
			Plugin.Log.LogWarning("[scene] scene pack not installed at " + Root);
			return false;
		}
		_traderScene = Assembly.LoadFrom(dllPath).GetType("tarkin.tradermod.shared.TraderScene");
		_camPoint = _traderScene?.GetProperty("CameraPoint", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		_traderGo = _traderScene?.GetProperty("TraderGameObject", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		return _traderScene != null && _camPoint != null;
	}

	private static void Animate(GameObject[] roots, ClientDialogController dc)
	{
		Component component = ((roots.Length != 0) ? roots[0].GetComponent(_traderScene) : null);
		Animator animator = ((component != null) ? (_traderGo?.GetValue(component) as Animator) : null);
		if ((object)animator == null)
		{
			animator = roots.SelectMany((GameObject r) => r.GetComponentsInChildren<Animator>(includeInactive: true)).FirstOrDefault((Animator a) => a.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true).Length != 0);
		}
		if (animator == null || dc == null)
		{
			Plugin.Log.LogWarning("[scene] no trader model found - lines will play silent");
			return;
		}
		NPCObject npc = animator.GetComponent<NPCObject>() ?? animator.gameObject.AddComponent<NPCObject>();
		dc._animationController = new TraderAnimationController(npc, dc);
	}

	internal static Transform FindCameraPoint(Scene scene)
	{
		if (!scene.isLoaded || !Bind())
		{
			return null;
		}
		return CameraPoint(scene.GetRootGameObjects());
	}

	private static Transform CameraPoint(GameObject[] roots)
	{
		Component component = ((roots.Length != 0) ? roots[0].GetComponent(_traderScene) : null);
		if (component != null && _camPoint.GetValue(component) is Transform result)
		{
			return result;
		}
		return roots.SelectMany((GameObject r) => r.GetComponentsInChildren<Transform>(includeInactive: true)).FirstOrDefault((Transform x) => x.name.StartsWith("Position_Camera", StringComparison.OrdinalIgnoreCase));
	}

	private static AssetBundle Bundle(string file)
	{
		// 快照必须先于任何 bundle 加载, 否则 rip 包残缺 shader 副本会混入快照(见 DEV_NOTES #57)
		SceneShaders.Snapshot();
		if (_bundles.TryGetValue(file, out var cached) && cached != null)
		{
			return cached;
		}
		string bundlePath = Path.Combine(Root, "bundles", "vendors", file);
		if (!File.Exists(bundlePath))
		{
			Plugin.Log.LogWarning("[scene] bundle missing: " + bundlePath);
			return null;
		}
		return _bundles[file] = AssetBundle.LoadFromFile(bundlePath);
	}
}
