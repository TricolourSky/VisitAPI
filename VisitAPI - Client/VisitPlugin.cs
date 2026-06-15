using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace VisitAPI;

[BepInPlugin("com.spt.visitapi", "VisitAPI", "0.2.4")]
public sealed class VisitPlugin : BaseUnityPlugin
{
	private static class CursorLockBlockerPatch
	{
		internal static bool Prefix(bool __0)
		{
			return !VisitApiEscHandler.IsActive || __0;
		}
	}

	public const string PluginGuid = "com.spt.visitapi";

	public const string PluginName = "VisitAPI";

	public const string PluginVersion = "0.2.4";

	private ConfigEntry<KeyCode>? _coordKey;

	private ConfigEntry<KeyCode>? _questDumpKey;

	private Harmony? _harmony;

	private bool _wasInRaid;

	private float _nextRaidCheck;

	private readonly List<GameObject> _raidTriggers = new List<GameObject>();

	private bool _inHideout;

	private float _nextHideoutCheck;

	private readonly List<GameObject> _hideoutTriggers = new List<GameObject>();

	// 已生成触发器的键（TraderId/AreaType），避免周期重扫时重复生成
	private readonly HashSet<string> _spawnedHideoutTriggerKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private float _nextHideoutTriggerRescan;

	private UnityEngine.Object? _cachedGameWorld;

	private static Type? _gwBaseType;

	private static bool _gwBaseTypeLookupDone;

	private static PropertyInfo? _gwInstanceProp;

	private static bool _gwLookupDone;

	internal static ManualLogSource Log { get; private set; }

	internal static ConfigEntry<bool> Enabled { get; private set; }

	internal static UnityEngine.Object? ActiveGameWorld { get; private set; }

	private void Awake()
	{
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_007c: Expected O, but got Unknown
		Log = Logger;
		Enabled = Config.Bind<bool>("General", "Enabled", true, "Enable/Disable Visit override");
		_coordKey = Config.Bind<KeyCode>("Debug", "CoordLogKey", (KeyCode)289, "Press in-raid to log current player position to BepInEx console");
		_questDumpKey = Config.Bind<KeyCode>("Debug", "QuestDumpKey", (KeyCode)288, "Press to dump all native quests' id->status to BepInEx console (verify external WTT quests are readable)");
		if (!Enabled.Value)
		{
			Log.LogInfo((object)"VisitAPI disabled");
			return;
		}
		_harmony = new Harmony("com.spt.visitapi");
		try
		{
			// 藏身处收藏配方 NRE 防护（必须始终安装，详见 FavoriteSchemeGuard）
			TryPatchFavoriteSchemeGuard(_harmony);
			TraderDealScreenHook.TryPatch(_harmony);
			TraderDialogScreenPatch.TryPatch(_harmony);
			TraderDialogWindowOptionRowPatch.TryPatch(_harmony);
			TryPatchCursorLock(_harmony);
			Log.LogInfo((object)"Harmony patches applied");
		}
		catch (Exception ex)
		{
			Log.LogError((object)ex);
		}
	}

	private void OnDestroy()
	{
		try
		{
			Harmony? harmony = _harmony;
			if (harmony != null)
			{
				harmony.UnpatchSelf();
			}
		}
		catch
		{
		}
	}

	private static void TryPatchCursorLock(Harmony harmony)
	{
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Expected O, but got Unknown
		MethodInfo methodInfo = AccessTools.TypeByName("GClass2304")?.GetMethod("smethod_0", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
		if (methodInfo == null)
		{
			Log.LogWarning((object)"CursorLockBlockerPatch: GClass2304.smethod_0 not found — click fix inactive");
			return;
		}
		harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(CursorLockBlockerPatch), "Prefix", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
		Log.LogInfo((object)"CursorLockBlockerPatch applied");
	}

	// 始终启用的防护：WTT 自定义藏身处配方注入与 EFT 后台 HideoutClass.Init 读取收藏数据存在竞态，
	// 竞态输时 PlayerPrefHelperClass.TryGetFavoriteIndex 内部读到 null → NRE，整个藏身处初始化中断，
	// 制作配方/发电机全部失效（任何额外 dll 在场扰动时序都可能触发）。这里挂 finalizer 吞掉异常、
	// 当作“无收藏”返回，让 Init 正常跑完。
	private static bool _favGuardLogged;

	private static void TryPatchFavoriteSchemeGuard(Harmony harmony)
	{
		Type type = AccessTools.TypeByName("PlayerPrefHelperClass");
		MethodInfo methodInfo = type?.GetMethod("TryGetFavoriteIndex", BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (methodInfo == null)
		{
			Log.LogWarning((object)"FavoriteSchemeGuard: PlayerPrefHelperClass.TryGetFavoriteIndex not found — guard inactive");
			return;
		}
		harmony.Patch((MethodBase)methodInfo, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, new HarmonyMethod(typeof(VisitPlugin), "FavoriteIndexFinalizer", (Type[])null), (HarmonyMethod)null);
		Log.LogInfo((object)"FavoriteSchemeGuard applied (TryGetFavoriteIndex)");
	}

	private static Exception? FavoriteIndexFinalizer(Exception __exception, ref int index, ref bool __result)
	{
		if (__exception != null)
		{
			index = -1;
			__result = false;
			if (!_favGuardLogged)
			{
				_favGuardLogged = true;
				Log.LogWarning((object)("[FavoriteSchemeGuard] swallowed exception in TryGetFavoriteIndex; hideout favorites treated as empty. " + __exception.GetType().Name));
			}
			return null;
		}
		return __exception;
	}

	private void Update()
	{
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		if (_coordKey != null && Input.GetKeyDown(_coordKey.Value))
		{
			LogPlayerPosition();
		}
		if (_questDumpKey != null && Input.GetKeyDown(_questDumpKey.Value))
		{
			NativeQuestController.DumpAllQuests();
		}
		TrackRaidLifecycle();
		TrackHideoutLifecycle();
	}

	private void TrackRaidLifecycle()
	{
		if (_inHideout) return;
		if (!(Time.unscaledTime < _nextRaidCheck))
		{
			_nextRaidCheck = Time.unscaledTime + 1f;
			bool flag = _cachedGameWorld != (UnityEngine.Object)null;
			if (!flag)
			{
				object obj = TryGetGameWorld();
				_cachedGameWorld = (UnityEngine.Object?)((obj is UnityEngine.Object) ? obj : null);
				ActiveGameWorld = _cachedGameWorld;
				flag = _cachedGameWorld != (UnityEngine.Object)null;
			}
			// Reject HideoutGameWorld — hideout lifecycle is handled separately
			if (flag && _cachedGameWorld != null
				&& _cachedGameWorld.GetType().Name.IndexOf("Hideout", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				_cachedGameWorld = null;
				ActiveGameWorld = null;
				flag = false;
			}
			if (flag && !_wasInRaid)
			{
				OnRaidStart();
			}
			else if (!flag && _wasInRaid)
			{
				_cachedGameWorld = null;
				ActiveGameWorld = null;
				OnRaidEnd();
			}
			_wasInRaid = flag;
		}
	}

	private void OnRaidStart()
	{
		Log.LogInfo((object)"RaidInteractTrigger: raid start detected, scanning for triggers");
		foreach (string traderId in DialogTreeLoader.EnumerateTraderIds())
		{
			FirstVisitTrigger firstVisitTrigger = DialogTreeLoader.TryLoad(traderId)?.FirstVisitTrigger;
			if (firstVisitTrigger != null && string.Equals(firstVisitTrigger.Type, "interact", StringComparison.OrdinalIgnoreCase) && firstVisitTrigger.Position != null && firstVisitTrigger.Position.Length >= 3)
			{
				GameObject val = new GameObject("VisitAPI.RaidTrigger." + traderId);
				UnityEngine.Object.DontDestroyOnLoad((UnityEngine.Object)(object)val);
				RaidInteractTrigger raidInteractTrigger = val.AddComponent<RaidInteractTrigger>();
				raidInteractTrigger.TraderId = traderId;
				raidInteractTrigger.TriggerPosition = new Vector3(firstVisitTrigger.Position[0], firstVisitTrigger.Position[1], firstVisitTrigger.Position[2]);
				raidInteractTrigger.MaxDistance = firstVisitTrigger.MaxDistance;
				raidInteractTrigger.HitRadius = firstVisitTrigger.HitRadius;
				raidInteractTrigger.PromptText = firstVisitTrigger.PromptText;
				raidInteractTrigger.DoorWidth = firstVisitTrigger.DoorWidth;
				raidInteractTrigger.DoorHeight = firstVisitTrigger.DoorHeight;
				raidInteractTrigger.DoorRotationY = firstVisitTrigger.DoorRotationY;
				_raidTriggers.Add(val);
				Log.LogInfo((object)$"RaidInteractTrigger: spawned for {traderId} at {raidInteractTrigger.TriggerPosition}");
			}
		}
	}

	private void OnRaidEnd()
	{
		foreach (GameObject raidTrigger in _raidTriggers)
		{
			try
			{
				if ((UnityEngine.Object)(object)raidTrigger != (UnityEngine.Object)null)
				{
					UnityEngine.Object.Destroy((UnityEngine.Object)(object)raidTrigger);
				}
			}
			catch
			{
			}
		}
		_raidTriggers.Clear();
		RandomDialogStore.RollForAllTraders();
		Log.LogInfo((object)"RaidInteractTrigger: all triggers destroyed (raid ended)");
		NativeQuestController.Sync(NativeQuestController.LastKnownProfileId);
	}

	internal static UnityEngine.Object? CachedHideoutOwner;

	internal static Type? HpoType;

	private static bool HpoTypeDone;

	private void TrackHideoutLifecycle()
	{
		if (Time.unscaledTime < _nextHideoutCheck)
			return;
		_nextHideoutCheck = Time.unscaledTime + 2f;

		// 战局内不可能进入藏身处：跳过 FindObjectOfType 全场景扫描
		// （raid 场景物体极多，每秒扫一次会造成周期性卡顿）
		if (_wasInRaid)
			return;

		bool nowInHideout;
		if (CachedHideoutOwner != null)
		{
			nowInHideout = true;
		}
		else
		{
			CachedHideoutOwner = null;
			if (!HpoTypeDone)
			{
				HpoTypeDone = true;
				HpoType = AccessTools.TypeByName("EFT.HideoutPlayerOwner")
					?? AccessTools.TypeByName("EFT.Hideout.HideoutPlayerOwner");
				if (HpoType == null)
				{
					foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
					{
						try
						{
							HpoType = Array.Find(asm.GetTypes(), t => t.Name == "HideoutPlayerOwner");
							if (HpoType != null) break;
						}
						catch { }
					}
				}
				if (HpoType != null)
					Log.LogInfo("[HideoutTrigger] HideoutPlayerOwner type: " + HpoType.FullName);
				else
					Log.LogWarning((object)"[HideoutTrigger] HideoutPlayerOwner type not found — hideout detection disabled");
			}
			if (HpoType != null)
				CachedHideoutOwner = UnityEngine.Object.FindObjectOfType(HpoType);
			nowInHideout = CachedHideoutOwner != null;
		}

		if (nowInHideout && !_inHideout)
			OnHideoutEntered();
		else if (!nowInHideout && _inHideout)
		{
			CachedHideoutOwner = null;
			OnHideoutExited();
		}
		else if (nowInHideout && Time.unscaledTime >= _nextHideoutTriggerRescan)
		{
			// 驻留期间周期重扫：进入时因区域等级不足/未初始化而被跳过的触发器，
			// 升级完成后要能补生成（否则要出去再进来才会重扫）
			_nextHideoutTriggerRescan = Time.unscaledTime + 10f;
			ScanHideoutTriggers(initial: false);
		}
		_inHideout = nowInHideout;
	}

	// 收集藏身处触发器：.dlg/.json 对话文件内嵌的优先，旧版 hideout_triggers.json 兼容保留
	//（同一"商人+区域"只取一个，内嵌定义覆盖旧文件）
	private static List<HideoutAreaTrigger> CollectHideoutTriggers()
	{
		List<HideoutAreaTrigger> list = new List<HideoutAreaTrigger>();
		HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string traderId in DialogTreeLoader.EnumerateTraderIds())
		{
			List<HideoutAreaTrigger>? embedded = DialogTreeLoader.TryLoad(traderId)?.HideoutTriggers;
			if (embedded == null)
			{
				continue;
			}
			foreach (HideoutAreaTrigger t in embedded)
			{
				if (string.IsNullOrEmpty(t.TraderId))
				{
					t.TraderId = traderId;
				}
				if (keys.Add(t.TraderId + "/" + t.AreaType))
				{
					list.Add(t);
				}
			}
		}
		string path = Path.Combine(BepInEx.Paths.ConfigPath, "VisitAPI", "hideout_triggers.json");
		if (File.Exists(path))
		{
			try
			{
				HideoutTriggerConfig? config = JsonConvert.DeserializeObject<HideoutTriggerConfig>(File.ReadAllText(path));
				if (config != null)
				{
					foreach (HideoutAreaTrigger t in config.Triggers)
					{
						if (keys.Add(t.TraderId + "/" + t.AreaType))
						{
							list.Add(t);
						}
					}
				}
			}
			catch (Exception ex)
			{
				Log.LogWarning((object)("[HideoutTrigger] Failed to parse hideout_triggers.json: " + ex.Message));
			}
		}
		return list;
	}

	private void OnHideoutEntered()
	{
		Log.LogInfo((object)"[HideoutTrigger] Hideout entered, scanning triggers");
		_spawnedHideoutTriggerKeys.Clear();
		_nextHideoutTriggerRescan = Time.unscaledTime + 10f;
		ScanHideoutTriggers(initial: true);
	}

	// initial=false 时为驻留期周期重扫，只补生成此前被跳过的触发器，且不重复刷日志
	private void ScanHideoutTriggers(bool initial)
	{
		List<HideoutAreaTrigger> triggers = CollectHideoutTriggers();
		if (triggers.Count == 0)
		{
			if (initial)
			{
				Log.LogInfo((object)"[HideoutTrigger] No hideout triggers configured, skipping");
			}
			return;
		}
		foreach (HideoutAreaTrigger triggerCfg in triggers)
		{
			if (string.IsNullOrEmpty(triggerCfg.TraderId) || string.IsNullOrEmpty(triggerCfg.AreaType))
			{
				continue;
			}
			string key = triggerCfg.TraderId + "/" + triggerCfg.AreaType;
			if (_spawnedHideoutTriggerKeys.Contains(key))
			{
				continue;
			}
			(GameObject? areaObj, int areaLevel) = FindHideoutArea(triggerCfg.AreaType);
			if (areaObj == null)
			{
				if (initial)
				{
					Log.LogWarning((object)("[HideoutTrigger] Area not found: " + triggerCfg.AreaType + " (will re-check every 10s)"));
				}
				continue;
			}
			if (areaLevel < triggerCfg.RequiredLevel)
			{
				if (initial)
				{
					Log.LogInfo((object)$"[HideoutTrigger] {triggerCfg.AreaType} level {areaLevel} < required {triggerCfg.RequiredLevel}, skipping (will re-check every 10s)");
				}
				continue;
			}
			Vector3 pos = areaObj.transform.position;
			if (triggerCfg.Offset != null && triggerCfg.Offset.Length >= 3)
			{
				pos += new Vector3(triggerCfg.Offset[0], triggerCfg.Offset[1], triggerCfg.Offset[2]);
			}
			GameObject val = new GameObject("VisitAPI.HideoutTrigger." + triggerCfg.AreaType + "." + triggerCfg.TraderId);
			HideoutInteractTrigger trigger = val.AddComponent<HideoutInteractTrigger>();
			trigger.TraderId = triggerCfg.TraderId;
			trigger.NodeOverride = triggerCfg.Node;
			trigger.PromptText = triggerCfg.PromptText;
			trigger.MaxDistance = triggerCfg.MaxDistance;
			trigger.TriggerPosition = pos;
			trigger.QuestId = triggerCfg.QuestId;
			trigger.ShowWhenStatus = triggerCfg.ShowWhenStatus;
			_hideoutTriggers.Add(val);
			_spawnedHideoutTriggerKeys.Add(key);
			Log.LogInfo((object)$"[HideoutTrigger] Spawned for {triggerCfg.TraderId} at {pos} (area level {areaLevel})");
		}
	}

	private void OnHideoutExited()
	{
		foreach (GameObject go in _hideoutTriggers)
		{
			try
			{
				if ((UnityEngine.Object)(object)go != (UnityEngine.Object)null)
				{
					UnityEngine.Object.Destroy((UnityEngine.Object)(object)go);
				}
			}
			catch
			{
			}
		}
		_hideoutTriggers.Clear();
		_spawnedHideoutTriggerKeys.Clear();
		Log.LogInfo((object)"[HideoutTrigger] Exited hideout, triggers destroyed");
	}

	private static bool _s_areaTypeDumped;
	private static Func<UnityEngine.Object, object?>? _s_areaTypeGetter;
	private static bool _s_areaTypeGetterSearched;
	private static Func<UnityEngine.Object, int>? _s_areaLevelGetter;
	private static bool _s_areaLevelGetterSearched;

	private static (GameObject? obj, int level) FindHideoutArea(string areaTypeName)
	{
		Type? areaType = AccessTools.TypeByName("EFT.Hideout.HideoutArea")
			?? TraderDealScreenVisitButton.FindType("HideoutArea");
		if (areaType == null)
		{
			Log.LogWarning((object)"[HideoutTrigger] HideoutArea type not found");
			return (null, 0);
		}
		UnityEngine.Object[] all = UnityEngine.Object.FindObjectsOfType(areaType);

		if (!_s_areaTypeDumped && all.Length > 0)
		{
			_s_areaTypeDumped = true;
			// Warm up getter caches silently on first call — no diagnostic dump needed.
			GetAreaTypeValue(all[0], areaType);
		}

		foreach (UnityEngine.Object obj in all)
		{
			Component? comp = obj as Component;
			if ((UnityEngine.Object)(object)comp == (UnityEngine.Object)null) continue;

			object? typeVal = GetAreaTypeValue(obj, areaType);
			if (typeVal == null) continue;
			if (!string.Equals(typeVal.ToString(), areaTypeName, StringComparison.OrdinalIgnoreCase)) continue;

			int level = GetHideoutAreaLevel(obj, areaType);
			Log.LogInfo((object)$"[HideoutTrigger] Found area {areaTypeName} level={level} at {comp.transform.position}");
			return (comp.gameObject, level);
		}
		return (null, 0);
	}

	private static object? GetAreaTypeValue(UnityEngine.Object obj, Type areaType)
	{
		if (!_s_areaTypeGetterSearched)
		{
			_s_areaTypeGetterSearched = true;
			_s_areaTypeGetter = BuildAreaTypeGetter(areaType, obj);
			if (_s_areaTypeGetter == null)
				Log.LogWarning((object)"[HideoutTrigger] Could not build area type getter");
		}
		try { return _s_areaTypeGetter?.Invoke(obj); }
		catch { return null; }
	}

	private static Func<UnityEngine.Object, object?>? BuildAreaTypeGetter(Type areaType, UnityEngine.Object sample)
	{
		BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

		// 1. Direct enum property/field with "Area" in type name on HideoutArea itself
		foreach (PropertyInfo p in areaType.GetProperties(bf))
		{
			if (p.PropertyType.IsEnum && p.PropertyType.Name.IndexOf("Area", StringComparison.OrdinalIgnoreCase) >= 0)
			{ var cap = p; return o => cap.GetValue(o); }
		}
		foreach (FieldInfo f in areaType.GetFields(bf))
		{
			if (f.FieldType.IsEnum && f.FieldType.Name.IndexOf("Area", StringComparison.OrdinalIgnoreCase) >= 0)
			{ var cap = f; return o => cap.GetValue(o); }
		}

		// 2. Via AreaTemplate.Type (EAreaType enum lives on the template ScriptableObject)
		PropertyInfo? tProp = areaType.GetProperty("AreaTemplate", bf);
		if (tProp != null)
		{
			Type tType = tProp.PropertyType;
			try { object? s = tProp.GetValue(sample); if (s != null) tType = s.GetType(); } catch { }

			foreach (string n in new[] { "Type", "AreaType", "EAreaType", "areaType", "type" })
			{
				PropertyInfo? ep = tType.GetProperty(n, bf);
				if (ep != null && ep.PropertyType.IsEnum)
				{ var cT = tProp; var cE = ep; return o => { try { object? t = cT.GetValue(o); return t != null ? cE.GetValue(t) : null; } catch { return null; } }; }
				FieldInfo? ef = tType.GetField(n, bf);
				if (ef != null && ef.FieldType.IsEnum)
				{ var cT = tProp; var cF = ef; return o => { try { object? t = cT.GetValue(o); return t != null ? cF.GetValue(t) : null; } catch { return null; } }; }
			}

			foreach (PropertyInfo p in tType.GetProperties(bf))
			{
				if (p.PropertyType.IsEnum)
				{ var cT = tProp; var cP = p; return o => { try { object? t = cT.GetValue(o); return t != null ? cP.GetValue(t) : null; } catch { return null; } }; }
			}
			foreach (FieldInfo f in tType.GetFields(bf))
			{
				if (f.FieldType.IsEnum)
				{ var cT = tProp; var cF = f; return o => { try { object? t = cT.GetValue(o); return t != null ? cF.GetValue(t) : null; } catch { return null; } }; }
			}
		}

		// 3. Fallback: name search directly on HideoutArea
		foreach (string n in new[] { "AreaType", "Type", "EAreaType", "areaType", "type" })
		{
			PropertyInfo? p = areaType.GetProperty(n, bf);
			if (p != null) { var cap = p; return o => { try { return cap.GetValue(o); } catch { return null; } }; }
			FieldInfo? fi = areaType.GetField(n, bf);
			if (fi != null) { var cap = fi; return o => { try { return cap.GetValue(o); } catch { return null; } }; }
		}
		return null;
	}

	private static int GetHideoutAreaLevel(UnityEngine.Object areaObj, Type areaType)
	{
		if (!_s_areaLevelGetterSearched)
		{
			_s_areaLevelGetterSearched = true;
			_s_areaLevelGetter = BuildAreaLevelGetter(areaType, areaObj);
			if (_s_areaLevelGetter == null)
				Log.LogInfo((object)"[HideoutTrigger] level getter not built — defaulting to 0");
		}
		return _s_areaLevelGetter?.Invoke(areaObj) ?? 0;
	}

	private static Func<UnityEngine.Object, int>? BuildAreaLevelGetter(Type areaType, UnityEngine.Object sample)
	{
		BindingFlags bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

		// 1. Direct int on HideoutArea
		foreach (string n in new[] { "Level", "CurrentLevel", "level", "currentLevel", "CurrentUpgradeLevel" })
		{
			PropertyInfo? p = areaType.GetProperty(n, bf);
			if (p?.PropertyType == typeof(int)) { var c = p; return o => { try { return (int)(c.GetValue(o) ?? 0); } catch { return 0; } }; }
			FieldInfo? f = areaType.GetField(n, bf);
			if (f?.FieldType == typeof(int)) { var c = f; return o => { try { return (int)(c.GetValue(o) ?? 0); } catch { return 0; } }; }
		}

		// 2. Data.CurrentLevel / Data.Level
		PropertyInfo? dataProp = areaType.GetProperty("Data", bf);
		if (dataProp != null)
		{
			Type dType = dataProp.PropertyType;
			try { object? s = dataProp.GetValue(sample); if (s != null) dType = s.GetType(); } catch { }
			foreach (string n in new[] { "CurrentLevel", "Level", "currentLevel", "level" })
			{
				PropertyInfo? p = dType.GetProperty(n, bf);
				if (p?.PropertyType == typeof(int))
				{ var cD = dataProp; var cP = p; return o => { try { object? d = cD.GetValue(o); return d != null ? (int)(cP.GetValue(d) ?? 0) : 0; } catch { return 0; } }; }
				FieldInfo? f = dType.GetField(n, bf);
				if (f?.FieldType == typeof(int))
				{ var cD = dataProp; var cF = f; return o => { try { object? d = cD.GetValue(o); return d != null ? (int)(cF.GetValue(d) ?? 0) : 0; } catch { return 0; } }; }
			}
		}

		// 3. CurrentLevel object → Level/Index int
		PropertyInfo? lvlProp = areaType.GetProperty("CurrentLevel", bf);
		if (lvlProp != null && lvlProp.PropertyType != typeof(int))
		{
			Type lType = lvlProp.PropertyType;
			try { object? s = lvlProp.GetValue(sample); if (s != null) lType = s.GetType(); } catch { }
			foreach (string n in new[] { "Level", "Index", "level", "index", "Number" })
			{
				PropertyInfo? p = lType.GetProperty(n, bf);
				if (p?.PropertyType == typeof(int))
				{ var cL = lvlProp; var cP = p; return o => { try { object? l = cL.GetValue(o); return l != null ? (int)(cP.GetValue(l) ?? 0) : 0; } catch { return 0; } }; }
			}
		}

		// 4. Fallback: Info.Level
		PropertyInfo? infoProp = areaType.GetProperty("Info", bf);
		if (infoProp != null)
		{
			Type iType = infoProp.PropertyType;
			try { object? s = infoProp.GetValue(sample); if (s != null) iType = s.GetType(); } catch { }
			PropertyInfo? lp = iType.GetProperty("Level", bf);
			if (lp?.PropertyType == typeof(int))
			{ var cI = infoProp; var cL2 = lp; return o => { try { object? i = cI.GetValue(o); return i != null ? (int)(cL2.GetValue(i) ?? 0) : 0; } catch { return 0; } }; }
		}
		return null;
	}

	internal static object? TryGetGameWorld()
	{
		if (!_gwBaseTypeLookupDone)
		{
			_gwBaseTypeLookupDone = true;
			_gwBaseType = AccessTools.TypeByName("EFT.GameWorld");
		}
		Type gwBaseType = _gwBaseType;
		if (gwBaseType != null)
		{
			UnityEngine.Object val = UnityEngine.Object.FindObjectOfType(gwBaseType);
			if (val != (UnityEngine.Object)null)
			{
				return val;
			}
			string[] array = new string[1] { "EFT.ClientGameWorld" };
			for (int i = 0; i < array.Length; i++)
			{
				Type type = AccessTools.TypeByName(array[i]);
				if (!(type == null))
				{
					val = UnityEngine.Object.FindObjectOfType(type);
					if (val != (UnityEngine.Object)null)
					{
						return val;
					}
				}
			}
		}
		if (!_gwLookupDone)
		{
			_gwLookupDone = true;
			if (gwBaseType != null)
			{
				Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
				foreach (Assembly assembly in assemblies)
				{
					try
					{
						Type[] types = assembly.GetTypes();
						foreach (Type type2 in types)
						{
							if (!type2.IsGenericType)
							{
								continue;
							}
							Type[] genericArguments = type2.GetGenericArguments();
							if (genericArguments.Length == 1 && !(genericArguments[0] != gwBaseType) && type2.GetGenericTypeDefinition().Name.StartsWith("Singleton"))
							{
								PropertyInfo property = type2.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);
								if (property != null)
								{
									_gwInstanceProp = property;
									break;
								}
							}
						}
					}
					catch
					{
					}
					if (_gwInstanceProp != null)
					{
						break;
					}
				}
			}
		}
		return _gwInstanceProp?.GetValue(null);
	}

	private static void LogPlayerPosition()
	{
		//IL_0158: Unknown result type (might be due to invalid IL or missing references)
		//IL_0167: Unknown result type (might be due to invalid IL or missing references)
		//IL_0176: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_008b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0090: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c5: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			// In hideout, TryGetGameWorld returns null (rejected); use Camera.main directly
			object? obj = TryGetGameWorld() ?? ActiveGameWorld;
			if (obj == null)
			{
				Camera? camFallback = Camera.main;
				if (camFallback != null)
				{
					Vector3 posFallback = camFallback.transform.position;
					Log.LogInfo($"[CoordLog] hideout  position=[{posFallback.x:F2}, {posFallback.y:F2}, {posFallback.z:F2}]");
					Log.LogInfo($"[CoordLog] JSON offset: \"offset\": [{posFallback.x:F2}, {posFallback.y:F2}, {posFallback.z:F2}]");
				}
				else
				{
					Log.LogWarning("[CoordLog] Not in raid/hideout");
				}
				return;
			}
			Type type = obj.GetType();
			PropertyInfo propertyInfo = null;
			Type type2 = type;
			while (type2 != null && type2 != typeof(object))
			{
				propertyInfo = type2.GetProperty("MainPlayer", BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (propertyInfo != null)
				{
					break;
				}
				type2 = type2.BaseType;
			}
			object obj2 = propertyInfo?.GetValue(obj);
			Component val = (Component)((obj2 is Component) ? obj2 : null);
			Vector3 position;
			if ((UnityEngine.Object)(object)val != (UnityEngine.Object)null)
			{
				position = val.transform.position;
			}
			else
			{
				Camera main = Camera.main;
				if ((UnityEngine.Object)(object)main == (UnityEngine.Object)null)
				{
					Log.LogWarning((object)"[CoordLog] Player not spawned yet — wait until fully in-raid, then press F8 again");
					return;
				}
				position = ((Component)main).transform.position;
				Log.LogInfo((object)"[CoordLog] (using Camera.main — spawn in first for precise coords)");
			}
			string text = "unknown";
			try
			{
				Type type3 = type;
				while (type3 != null && type3 != typeof(object))
				{
					PropertyInfo property = type3.GetProperty("Location", BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					if (!(property == null))
					{
						text = (property.GetValue(obj) as string) ?? "unknown";
						break;
					}
					type3 = type3.BaseType;
				}
			}
			catch
			{
			}
			Log.LogInfo((object)$"[CoordLog] map={text}  position=[{position.x:F2}, {position.y:F2}, {position.z:F2}]");
			Log.LogInfo((object)$"[CoordLog] JSON snippet: \"map\": \"{text}\", \"position\": [{position.x:F2}, {position.y:F2}, {position.z:F2}]");
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[CoordLog] " + ex.Message));
		}
	}

	// 在 GameWorld 上查找本地玩家：先试直接属性/字段，再遍历玩家列表找 IsYourPlayer
	private static object? FindLocalPlayer(object gameWorld)
	{
		Type type = gameWorld.GetType();
		foreach (string name in new[] { "MainPlayer", "LocalPlayer", "_player", "Player", "_mainPlayer", "ClientPlayer", "mainPlayer" })
		{
			object? player = AccessTools.Property(type, name)?.GetValue(gameWorld) ?? AccessTools.Field(type, name)?.GetValue(gameWorld);
			if (player != null)
			{
				return player;
			}
		}
		foreach (string name in new[] { "AllAlivePlayersList", "RegisteredPlayers", "AllPlayers", "_players", "Players" })
		{
			if ((AccessTools.Property(type, name)?.GetValue(gameWorld) ?? AccessTools.Field(type, name)?.GetValue(gameWorld)) is IEnumerable list)
			{
				foreach (object item in list)
				{
					if (item != null && ((AccessTools.Property(item.GetType(), "IsYourPlayer")?.GetValue(item) as bool?) ?? (AccessTools.Field(item.GetType(), "IsYourPlayer")?.GetValue(item) as bool?)).GetValueOrDefault())
					{
						return item;
					}
				}
			}
		}
		return null;
	}

	internal static bool TryGetProfileInfo(out string profileId, out string playerName)
	{
		profileId = "";
		playerName = "";
		try
		{
			object obj = ActiveGameWorld ?? TryGetGameWorld();
			if (obj == null)
			{
				return false;
			}
			object obj2 = FindLocalPlayer(obj);
			if (obj2 == null)
			{
				return false;
			}
			Type type2 = obj2.GetType();
			object? obj3 = null;
			foreach (string pn in new[] { "Profile", "_profile", "profile", "PlayerProfile", "_playerProfile" })
			{
				obj3 = AccessTools.Property(type2, pn)?.GetValue(obj2) ?? AccessTools.Field(type2, pn)?.GetValue(obj2);
				if (obj3 != null) { Log.LogInfo("[TryGetProfileInfo] profile via '" + pn + "'"); break; }
			}
			if (obj3 == null)
			{
				var sbM = new System.Text.StringBuilder("[TryGetProfileInfo] Player members (Profile not found):\n");
				BindingFlags bf2 = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
				foreach (PropertyInfo p in type2.GetProperties(bf2))
					sbM.Append($"  P:{p.Name}({p.PropertyType.Name})\n");
				foreach (FieldInfo f in type2.GetFields(bf2))
					sbM.Append($"  F:{f.Name}({f.FieldType.Name})\n");
				Log.LogInfo(sbM.ToString());
				return false;
			}
			Type type3 = obj3.GetType();
			foreach (string n in new[] { "Id", "_id", "AccountId", "_accountId", "accountId", "ProfileId", "_profileId", "id" })
			{
				profileId = (AccessTools.Property(type3, n)?.GetValue(obj3) ?? AccessTools.Field(type3, n)?.GetValue(obj3)) as string ?? "";
				if (!string.IsNullOrEmpty(profileId)) break;
			}
			if (string.IsNullOrEmpty(profileId))
			{
				// Dump Profile members for diagnosis
				var sbP = new System.Text.StringBuilder("[TryGetProfileInfo] Profile members:\n");
				BindingFlags bfP = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
				foreach (PropertyInfo p in type3.GetProperties(bfP))
				{ string v; try { v = p.GetValue(obj3)?.ToString() ?? "null"; } catch { v = "?"; } sbP.Append($"  P:{p.Name}({p.PropertyType.Name})={v}\n"); }
				foreach (FieldInfo f in type3.GetFields(bfP))
				{ string v; try { v = f.GetValue(obj3)?.ToString() ?? "null"; } catch { v = "?"; } sbP.Append($"  F:{f.Name}({f.FieldType.Name})={v}\n"); }
				Log.LogInfo(sbP.ToString());
				return false;
			}
			object? obj4 = null;
			foreach (string n in new[] { "Info", "_info", "PlayerInfo", "_playerInfo", "info" })
			{
				obj4 = AccessTools.Property(type3, n)?.GetValue(obj3) ?? AccessTools.Field(type3, n)?.GetValue(obj3);
				if (obj4 != null) break;
			}
			if (obj4 != null)
			{
				foreach (string n in new[] { "Nickname", "_nickname", "NickName", "nickname" })
				{
					playerName = (AccessTools.Property(obj4.GetType(), n)?.GetValue(obj4) ?? AccessTools.Field(obj4.GetType(), n)?.GetValue(obj4)) as string ?? "";
					if (!string.IsNullOrEmpty(playerName)) break;
				}
			}
			if (string.IsNullOrEmpty(playerName))
			{
				foreach (string n in new[] { "Nickname", "_nickname", "NickName", "nickname" })
				{
					playerName = (AccessTools.Property(type3, n)?.GetValue(obj3) ?? AccessTools.Field(type3, n)?.GetValue(obj3)) as string ?? "";
					if (!string.IsNullOrEmpty(playerName)) break;
				}
			}
			return !string.IsNullOrEmpty(profileId);
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[TryGetProfileInfo] " + ex.Message));
			return false;
		}
	}

	internal static bool TryGetProfileFromCommandLine(out string profileId)
	{
		profileId = "";
		try
		{
			string[] args = Environment.GetCommandLineArgs();
			for (int i = 0; i < args.Length; i++)
			{
				if ((args[i] == "-token" || args[i] == "--token") && i + 1 < args.Length)
				{ profileId = args[i + 1].Trim(); break; }
				if (args[i].StartsWith("token:", StringComparison.OrdinalIgnoreCase))
				{ profileId = args[i].Substring(6).Trim(); break; }
			}
		}
		catch { }
		return !string.IsNullOrEmpty(profileId);
	}

	internal static string GetPluginDir()
	{
		return Path.GetDirectoryName(typeof(VisitPlugin).Assembly.Location) ?? "";
	}

	internal static bool IsTraderRegistered(string? traderId)
	{
		return DialogTreeLoader.IsRegistered(traderId);
	}

	internal static bool TryGetInRaidControllers(out object? profile, out object? questCtrl, out object? invCtrl)
	{
		profile = null;
		questCtrl = null;
		invCtrl = null;
		try
		{
			object obj = ActiveGameWorld ?? TryGetGameWorld();
			if (obj == null)
			{
				return false;
			}
			object obj2 = FindLocalPlayer(obj);
			if (obj2 == null)
			{
				return false;
			}
			Type type2 = obj2.GetType();
			profile = AccessTools.Property(type2, "Profile")?.GetValue(obj2) ?? AccessTools.Field(type2, "Profile")?.GetValue(obj2) ?? AccessTools.Field(type2, "profile")?.GetValue(obj2);
			string[] array = new string[4] { "AbstractQuestControllerClass", "QuestController", "_questController", "questController" };
			foreach (string text3 in array)
			{
				questCtrl = AccessTools.Property(type2, text3)?.GetValue(obj2) ?? AccessTools.Field(type2, text3)?.GetValue(obj2);
				if (questCtrl != null)
				{
					break;
				}
			}
			array = new string[4] { "InventoryController", "_inventoryController", "inventoryController", "InventoryController_0" };
			foreach (string text4 in array)
			{
				invCtrl = AccessTools.Property(type2, text4)?.GetValue(obj2) ?? AccessTools.Field(type2, text4)?.GetValue(obj2);
				if (invCtrl != null)
				{
					break;
				}
			}
			if (profile != null)
			{
				Log.LogInfo((object)$"[InRaidControllers] profile={profile != null} quest={questCtrl != null} inv={invCtrl != null}");
			}
			return profile != null;
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[TryGetInRaidControllers] " + ex.Message));
			return false;
		}
	}

	internal static bool TryGetVisitPngForTrader(string? traderId, out string absolutePath)
	{
		absolutePath = "";
		if (string.IsNullOrEmpty(traderId))
		{
			return false;
		}
		string text = Path.Combine(BepInEx.Paths.ConfigPath, "VisitAPI", traderId + ".png");
		if (File.Exists(text))
		{
			absolutePath = text;
			return true;
		}
		return false;
	}
}
