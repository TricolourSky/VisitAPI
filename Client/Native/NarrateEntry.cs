using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using EFT.Dialogs;
using EFT.EnvironmentEffect;
using EFT.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VisitAPI.Native;

public static class NarrateEntry
{
	private static bool _mapped;

	private static readonly FieldInfo MenuOpField = AccessTools.Field(typeof(TarkovApplication), "_menuOperation");

	public static bool CanVisit(string traderId)
	{
		if (!SceneLoader.HasRoom(traderId))
		{
			return false;
		}
		EnsureTraderTypeMap();
		if (!Profile.TraderInfo.TraderIdToType.TryGetValue(traderId, out var traderType))
		{
			return false;
		}
		if (!TarkovApplication.NarrateController.Scenes.IsValid(traderType, out var _) && !RegisterScene(traderId, traderType))
		{
			return false;
		}
		GlobalConfiguration instance = Singleton<GlobalConfiguration>.Instance;
		GlobalConfiguration.TraderSettings settings;
		return instance != null && instance.TradersSettings.TryGetValue(traderId, out settings) && settings != null && settings.MainDialog.HasValue;
	}

	private static bool RegisterScene(string traderId, Profile.ETraderType type)
	{
		AssetBundle assetBundle = SceneLoader.EnsureNarrateBundles(traderId);
		string[] scenePaths = ((assetBundle == null) ? null : assetBundle.GetAllScenePaths());
		if (scenePaths == null || scenePaths.Length == 0)
		{
			return false;
		}
		string sceneName = Path.GetFileNameWithoutExtension(scenePaths[0]);
		TarkovApplication.NarrateController.Scenes.narrateScenes[type] = new NarrateSceneInfo
		{
			sceneName = sceneName
		};
		Plugin.Log.LogInfo("[narrate] scene preset registered: " + type.ToString() + " -> " + sceneName);
		return true;
	}

	public static void Visit(string traderId)
	{
		Profile.ETraderType traderType;
		if (!TarkovApplication.Exist(out var tarkovApplication) || tarkovApplication.NarrateControllerAccess == null)
		{
			Plugin.Log.LogWarning("[narrate] application not ready");
		}
		else if (Profile.TraderInfo.TraderIdToType.TryGetValue(traderId, out traderType))
		{
			if (SceneLoader.EnsureNarrateBundles(traderId) == null)
			{
				Plugin.Log.LogWarning("[narrate] scene bundles missing for " + traderId);
				return;
			}
			WhitelistPatch.RegisteredTraders.Add(traderId);
			NarrateTabs.Watch(tarkovApplication, traderId);
			Plugin.Instance.StartCoroutine(Run(tarkovApplication, traderType));
		}
	}

	private static IEnumerator Run(TarkovApplication app, Profile.ETraderType type)
	{
		EnvironmentManager env = ((EnvironmentManager.Instance == null) ? new GameObject("VisitNarrateEnv").AddComponent<EnvironmentManager>() : null);
		Task task = app.NarrateControllerAccess.Show(type);
		while (!task.IsCompleted)
		{
			yield return null;
		}
		NarrateGameWorld world = (Singleton<GameWorld>.Instantiated ? (Singleton<GameWorld>.Instance as NarrateGameWorld) : null);
		if (env != null && world != null)
		{
			env.transform.SetParent(world.transform, worldPositionStays: false);
		}
		else if (env != null)
		{
			Object.Destroy(env.gameObject);
		}
		if (task.IsFaulted)
		{
			Plugin.Log.LogWarning("[narrate] show failed: " + task.Exception?.GetBaseException()?.Message);
			Abort(app);
			yield break;
		}
		if (!TarkovApplication.NarrateController.Scenes.IsValid(type, out var info))
		{
			Plugin.Log.LogWarning("[narrate] scene preset lost for " + type);
			Abort(app);
			yield break;
		}
		Scene scene = SceneManager.GetSceneByName(info.sceneName);
		Scene common = SceneManager.GetSceneByName("Vendors_Scripts");
		if (common.isLoaded)
		{
			GameObject[] commonRoots = common.GetRootGameObjects();
			foreach (GameObject root in commonRoots)
			{
				SceneShaders.Fix(root);
			}
		}
		SceneLighting.Apply(scene);
		SceneShaders.ReportMisses();
		// 原生 Show 后的若干帧内环境 UI/相机会被反复重隐藏, 连续压制到稳定为止
		for (int k = 0; k < 30; k++)
		{
			SceneLighting.Uncover(visiting: true);
			yield return null;
		}
		for (int l = 0; l < 180; l++)
		{
			if (Object.FindObjectOfType<TraderDialogScreen>() != null)
			{
				break;
			}
			yield return null;
		}
		if (Object.FindObjectOfType<TraderDialogScreen>() == null)
		{
			Plugin.Log.LogWarning("[narrate] dialog screen never opened - aborting visit (press F9 if still stuck)");
			Abort(app);
		}
	}

	public static void Abort(TarkovApplication app = null)
	{
		if (app == null && !TarkovApplication.Exist(out app))
		{
			Plugin.Log.LogWarning("[narrate] abort: no application");
			return;
		}
		if (app.NarrateControllerAccess == null)
		{
			Plugin.Log.LogWarning("[narrate] abort: no narrate controller");
			return;
		}
		if (!app.NarrateControllerAccess.GameExist)
		{
			Plugin.Log.LogWarning("[narrate] abort: no active visit");
			return;
		}
		app.NarrateControllerAccess.Hide();
		Plugin.Log.LogInfo("[narrate] visit aborted - back to menu");
	}

	public static void EnsureMenu()
	{
		// 必须先断开 Camera 再 Hide: 让 CameraNullGuard 短路 Hide 末尾的 IsActive=true, 否则刚关的相机会被复活(退出时序坑)
		if (CameraManager.Exist)
		{
			CameraManager.Instance.Camera = null;
		}
		SceneCamera.Hide();
		SceneLighting.Release();
		if (TarkovApplication.Exist(out var tarkovApplication) && MenuOpField?.GetValue(tarkovApplication) is MainMenuShowOperation op)
		{
			Plugin.Instance.StartCoroutine(MenuWatch(op));
		}
	}

	private static IEnumerator MenuWatch(MainMenuShowOperation op)
	{
		for (int i = 0; i < 90; i++)
		{
			yield return null;
			if (Object.FindObjectOfType<MenuScreen>() != null)
			{
				yield break;
			}
		}
		Plugin.Log.LogWarning("[narrate] main menu did not return - forcing it");
		op.ShowMenuScreenSync();
	}

	// 原生商人映射表缺两位: Mechanic(5a7c2eca...)没进正反两表, Jaeger(5c0647fd...)在本 build 枚举里没有命名成员——12 是 1.0 官方枚举表核对出的数值
	private static void EnsureTraderTypeMap()
	{
		if (!_mapped)
		{
			_mapped = true;
			Dictionary<MongoID, Profile.ETraderType> fwd = (Dictionary<MongoID, Profile.ETraderType>)Profile.TraderInfo.TraderIdToType;
			Dictionary<Profile.ETraderType, MongoID> rev = (Dictionary<Profile.ETraderType, MongoID>)Profile.TraderInfo.TraderTypeToId;
			Map(fwd, rev, "5a7c2eca46aef81a7ca2145d", Profile.ETraderType.Mechanic);
			Map(fwd, rev, "5c0647fdd443bc2504c2d371", (Profile.ETraderType)12);
		}
	}

	private static void Map(Dictionary<MongoID, Profile.ETraderType> fwd, Dictionary<Profile.ETraderType, MongoID> rev, MongoID id, Profile.ETraderType type)
	{
		if (!fwd.ContainsKey(id))
		{
			fwd.Add(id, type);
		}
		if (!rev.ContainsKey(type))
		{
			rev.Add(type, id);
		}
	}
}
