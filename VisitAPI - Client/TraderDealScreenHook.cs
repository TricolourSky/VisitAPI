using System;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace VisitAPI;

internal static class TraderDealScreenHook
{
	private static Component? _servicesAnchor;

	private static Component? _screensGroup;

	private static Component? _dealScreen;

	private static string? _lastBroadTraderId;

	private static Component? _pluginActivatedTab;

	private static bool _tabSelectPatched;

	private static Harmony? _tabSelectHarmony;

	internal static Component? ServicesAnchor => _servicesAnchor;

	internal static Component? ScreensGroup => _screensGroup;

	internal static Component? DealScreen => _dealScreen;

	public static void TryPatch(Harmony harmony)
	{
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Expected O, but got Unknown
		//IL_00bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c9: Expected O, but got Unknown
		//IL_0152: Unknown result type (might be due to invalid IL or missing references)
		//IL_015f: Expected O, but got Unknown
		//IL_01cf: Unknown result type (might be due to invalid IL or missing references)
		//IL_01dc: Expected O, but got Unknown
		//IL_0222: Unknown result type (might be due to invalid IL or missing references)
		//IL_0229: Expected O, but got Unknown
		_tabSelectHarmony = harmony;
		Type type = AccessTools.TypeByName("EFT.UI.TraderScreensGroup");
		if (type != null)
		{
			MethodBase methodBase = FindShowMethod(type);
			if (methodBase != null)
			{
				harmony.Patch(methodBase, (HarmonyMethod)null, new HarmonyMethod(typeof(TraderDealScreenHook), "GroupShowPostfix", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
				VisitPlugin.Log.LogInfo((object)("Patched trader screens group show: " + type.FullName + "." + methodBase.Name));
			}
		}
		Type type2 = AccessTools.TypeByName("EFT.UI.ServicesScreen") ?? TraderDealScreenVisitButton.FindType("ServicesScreen");
		if (type2 != null)
		{
			MethodBase methodBase2 = FindShowMethod(type2);
			if (methodBase2 != null)
			{
				harmony.Patch(methodBase2, (HarmonyMethod)null, new HarmonyMethod(typeof(TraderDealScreenHook), "NonTradeShowPostfix", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
				VisitPlugin.Log.LogInfo((object)("Patched services screen show: " + type2.FullName + "." + methodBase2.Name));
			}
		}
		Type type3 = FindTraderDealScreenType();
		if (type3 == null)
		{
			VisitPlugin.Log.LogWarning((object)"TraderDealScreen type not found; Visit button will not be injected");
			return;
		}
		MethodBase methodBase3 = FindShowMethod(type3);
		if (methodBase3 == null)
		{
			VisitPlugin.Log.LogWarning((object)("TraderDealScreen.Show method not found on " + type3.FullName + "; Visit button will not be injected"));
			return;
		}
		harmony.Patch(methodBase3, (HarmonyMethod)null, new HarmonyMethod(typeof(TraderDealScreenHook), "ShowPostfix", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
		VisitPlugin.Log.LogInfo((object)("Patched trader screen show: " + type3.FullName + "." + methodBase3.Name));
		MethodInfo methodInfo = AccessTools.GetDeclaredMethods(type3).FirstOrDefault((MethodInfo m) => string.Equals(m.Name, "OnTradingModeChanged", StringComparison.Ordinal));
		if (methodInfo != null)
		{
			harmony.Patch((MethodBase)methodInfo, (HarmonyMethod)null, new HarmonyMethod(typeof(TraderDealScreenHook), "ShowPostfix", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
			VisitPlugin.Log.LogInfo((object)("Patched trader mode changed: " + type3.FullName + "." + methodInfo.Name));
		}
		if (!(type != null))
		{
			return;
		}
		int num = 0;
		HarmonyMethod val = new HarmonyMethod(typeof(TraderDealScreenHook), "GroupAnyMethodPostfix", (Type[])null);
		foreach (MethodInfo item in from m in AccessTools.GetDeclaredMethods(type)
			where !m.IsAbstract && !m.IsSpecialName
			select m)
		{
			try
			{
				harmony.Patch((MethodBase)item, (HarmonyMethod)null, val, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
				num++;
			}
			catch
			{
			}
		}
		VisitPlugin.Log.LogInfo((object)$"TraderScreensGroup: broad-patched {num} methods for RITC fallback");
	}

	private static void GroupShowPostfix(object __instance, object[] __args)
	{
		try
		{
			Component val = (Component)((__instance is Component) ? __instance : null);
			if (val == null)
			{
				return;
			}
			_screensGroup = val;
			TryCacheProfileFromScreensGroup(val);
			object obj = ((object)val).GetType().GetField("_servicesTab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(val);
			_servicesAnchor = (Component?)((obj is Component) ? obj : null);
			if ((UnityEngine.Object)(object)_servicesAnchor != (UnityEngine.Object)null)
			{
				ManualLogSource log = VisitPlugin.Log;
				string[] obj2 = new string[6]
				{
					"Services anchor (direct field): ",
					((UnityEngine.Object)_servicesAnchor.gameObject).name,
					" (",
					((object)_servicesAnchor).GetType().Name,
					") parent=",
					null
				};
				Transform parent = _servicesAnchor.transform.parent;
				obj2[5] = ((parent != null) ? ((UnityEngine.Object)parent).name : null);
				log.LogInfo((object)string.Concat(obj2));
			}
			else
			{
				_servicesAnchor = FindServicesAnchor(val);
				if ((UnityEngine.Object)(object)_servicesAnchor != (UnityEngine.Object)null)
				{
					VisitPlugin.Log.LogInfo((object)("Services anchor (heuristic): " + ((UnityEngine.Object)_servicesAnchor.gameObject).name));
				}
				else
				{
					VisitPlugin.Log.LogWarning((object)"Services anchor not found on TraderScreensGroup");
				}
			}
			string text = TryExtractTraderId(__args) ?? TryFindTraderIdOnInstance(__instance) ?? TryFindTraderIdDeep(__instance);
			VisitPlugin.Log.LogInfo((object)("TraderScreensGroup.Show traderId=" + (text ?? "(null)")));
			if (text != null && DialogTreeLoader.IsRegistered(text))
			{
				(val.gameObject.GetComponent<VisitButtonPendingInjector>() ?? val.gameObject.AddComponent<VisitButtonPendingInjector>()).Arm(_servicesAnchor, text);
			}
		}
		catch (Exception ex)
		{
			VisitPlugin.Log.LogError((object)ex);
		}
	}

	private static void TryCacheProfileFromScreensGroup(Component screensGroup)
	{
		Component screensGroup2 = screensGroup;
		try
		{
			Type sgType = ((object)screensGroup2).GetType();
			object obj = Resolve(new string[4] { "Profile_0", "Profile", "_profile", "profile" });
			object questCtrl = Resolve(new string[4] { "AbstractQuestControllerClass", "questController", "QuestController", "_questController" });
			object invCtrl = Resolve(new string[4] { "InventoryController_0", "InventoryController", "_inventoryController", "inventoryController" });
			if (obj == null)
			{
				return;
			}
			TraderDealScreenVisitButton.SetCachedControllers(obj, questCtrl, invCtrl);
			if (TraderDealScreenVisitButton.TryGetCachedProfile(out string _, out string _))
			{
				return;
			}
			Type type = obj.GetType();
			string text = ((type.GetProperty("Id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj) ?? type.GetField("Id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj)) as string) ?? "";
			if (!string.IsNullOrEmpty(text))
			{
				string text2 = "";
				object obj2 = type.GetProperty("Info", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj) ?? type.GetField("Info", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj);
				if (obj2 != null)
				{
					text2 = ((obj2.GetType().GetProperty("Nickname", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj2) ?? obj2.GetType().GetField("Nickname", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj2)) as string) ?? "";
				}
				TraderDealScreenVisitButton.SetCachedProfile(text, text2);
				VisitPlugin.Log.LogInfo((object)("[TryCacheProfile] id='" + text + "' name='" + text2 + "'"));
			}
			object? Resolve(string[] names)
			{
				foreach (string name in names)
				{
					object obj3 = sgType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(screensGroup2) ?? sgType.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(screensGroup2);
					if (obj3 != null)
					{
						return obj3;
					}
				}
				return null;
			}
		}
		catch (Exception ex)
		{
			VisitPlugin.Log.LogWarning((object)("[TryCacheProfile] " + ex.Message));
		}
	}

	private static void ShowPostfix(object __instance, object[] __args)
	{
		try
		{
			TraderDealScreenVisitButton.ActiveNativeClose?.Invoke();
			Component val = (Component)((__instance is Component) ? __instance : null);
			if (val == null)
			{
				return;
			}
			_dealScreen = val;
			Component? screensGroup = _screensGroup;
			if (screensGroup != null)
			{
				screensGroup.gameObject.GetComponent<VisitButtonPendingInjector>()?.Cancel();
			}
			if ((UnityEngine.Object)(object)_servicesAnchor == (UnityEngine.Object)null && (UnityEngine.Object)(object)_screensGroup != (UnityEngine.Object)null)
			{
				_servicesAnchor = FindServicesAnchor(_screensGroup);
				if ((UnityEngine.Object)(object)_servicesAnchor != (UnityEngine.Object)null)
				{
					VisitPlugin.Log.LogInfo((object)("Services anchor recovered: " + ((UnityEngine.Object)_servicesAnchor.gameObject).name + " (" + ((object)_servicesAnchor).GetType().FullName + ")"));
				}
			}
			string text = TryExtractTraderId(__args) ?? TryFindTraderIdOnInstance(__instance);
			ManualLogSource log = VisitPlugin.Log;
			string obj = text ?? "(null)";
			Component? servicesAnchor = _servicesAnchor;
			log.LogInfo((object)("TraderDealScreen.Show traderId=" + obj + " servicesAnchor=" + (((servicesAnchor != null) ? ((UnityEngine.Object)servicesAnchor.gameObject).name : null) ?? "(null)")));
			(val.gameObject.GetComponent<TraderDealScreenVisitButton>() ?? val.gameObject.AddComponent<TraderDealScreenVisitButton>()).Refresh(_servicesAnchor, text);
			if (TraderDealScreenVisitButton.PluginActivatedTasksTab)
			{
				TraderDealScreenVisitButton.PluginActivatedTasksTab = false;
				HideQuestsScreen(_screensGroup);
			}
		}
		catch (Exception ex)
		{
			VisitPlugin.Log.LogError((object)ex);
		}
	}

	private static void GroupAnyMethodPostfix(object __instance, object[] __args)
	{
		try
		{
			Component val = (Component)((__instance is Component) ? __instance : null);
			if (val == null)
			{
				return;
			}
			string text = TryExtractTraderId(__args ?? Array.Empty<object>()) ?? TryFindTraderIdOnInstance(__instance);
			if (text == null)
			{
				return;
			}
			VisitButtonPendingInjector component = val.gameObject.GetComponent<VisitButtonPendingInjector>();
			if (!string.Equals(text, _lastBroadTraderId, StringComparison.Ordinal))
			{
				component?.Cancel();
			}
			TraderDealScreenVisitButton component2 = val.gameObject.GetComponent<TraderDealScreenVisitButton>();
			if ((UnityEngine.Object)(object)component2 != (UnityEngine.Object)null)
			{
				component2.Refresh(_servicesAnchor, text);
			}
			if (DialogTreeLoader.IsRegistered(text) && !string.Equals(text, _lastBroadTraderId, StringComparison.Ordinal))
			{
				_lastBroadTraderId = text;
				if ((UnityEngine.Object)(object)_servicesAnchor == (UnityEngine.Object)null)
				{
					_servicesAnchor = FindServicesAnchor(val);
				}
				component = component ?? val.gameObject.AddComponent<VisitButtonPendingInjector>();
				component.Arm(_servicesAnchor, text);
			}
		}
		catch
		{
		}
	}

	private static void NonTradeShowPostfix()
	{
		try
		{
			TraderDealScreenVisitButton.ActiveNativeClose?.Invoke();
			if (TraderDealScreenVisitButton.PluginActivatedTasksTab)
			{
				TraderDealScreenVisitButton.PluginActivatedTasksTab = false;
				HideQuestsScreen(_screensGroup);
			}
		}
		catch (Exception ex)
		{
			VisitPlugin.Log.LogError((object)ex);
		}
	}

	private static void HideQuestsScreen(Component? sg)
	{
		if ((UnityEngine.Object)(object)sg == (UnityEngine.Object)null)
		{
			return;
		}
		string[] array = new string[3] { "_tasksTab", "_questsTab", "_questTab" };
		foreach (string name in array)
		{
			object obj = ((object)sg).GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(sg);
			Component val = (Component)((obj is Component) ? obj : null);
			if ((UnityEngine.Object)(object)val == (UnityEngine.Object)null)
			{
				continue;
			}
			MonoBehaviour[] components = val.GetComponents<MonoBehaviour>();
			foreach (MonoBehaviour val2 in components)
			{
				if ((UnityEngine.Object)(object)val2 == (UnityEngine.Object)null)
				{
					continue;
				}
				MethodInfo method = ((object)val2).GetType().GetMethod("Deselect", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
				if (!(method == null) && !method.IsAbstract)
				{
					try
					{
						method.Invoke(val2, null);
					}
					catch
					{
					}
					break;
				}
			}
			break;
		}
		array = new string[3] { "_questsScreen", "_tasksScreen", "_questScreen" };
		foreach (string name2 in array)
		{
			object obj3 = ((object)sg).GetType().GetField(name2, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(sg);
			Component val3 = (Component)((obj3 is Component) ? obj3 : null);
			if (!((UnityEngine.Object)(object)val3 == (UnityEngine.Object)null))
			{
				try
				{
					val3.gameObject.SetActive(false);
				}
				catch
				{
				}
				VisitPlugin.Log.LogInfo((object)("HideQuestsScreen: hid " + ((UnityEngine.Object)val3.gameObject).name));
				break;
			}
		}
	}

	private static Type? FindTraderDealScreenType()
	{
		Type type = AccessTools.TypeByName("EFT.UI.TraderDealScreen");
		if (type != null)
		{
			return type;
		}
		Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
		foreach (Assembly assembly in assemblies)
		{
			Type[] array;
			try
			{
				array = assembly.GetTypes();
			}
			catch (ReflectionTypeLoadException ex)
			{
				array = ex.Types.Where((Type x) => x != null).Cast<Type>().ToArray();
			}
			catch
			{
				continue;
			}
			Type[] array2 = array;
			int num = 0;
			while (num < array2.Length)
			{
				Type type2 = array2[num];
				if (!(type2.Name == "TraderDealScreen"))
				{
					string fullName = type2.FullName;
					if (fullName == null || !fullName.EndsWith(".TraderDealScreen", StringComparison.Ordinal))
					{
						num++;
						continue;
					}
				}
				return type2;
			}
		}
		return null;
	}

	private static MethodBase? FindShowMethod(Type t)
	{
		MethodInfo[] source = (from m in AccessTools.GetDeclaredMethods(t)
			where !m.IsAbstract
			select m).ToArray();
		return source.FirstOrDefault((MethodInfo m) => m.Name == "Show") ?? source.FirstOrDefault((MethodInfo m) => m.Name.IndexOf("Show", StringComparison.OrdinalIgnoreCase) >= 0);
	}

	private static Component? FindServicesAnchor(Component root)
	{
		Selectable val = FindServicesSelectableByScoring(root);
		if ((UnityEngine.Object)(object)val != (UnityEngine.Object)null)
		{
			return (Component?)(object)val;
		}
		Transform val2 = root.GetComponentsInChildren<Transform>(true).FirstOrDefault((Transform x) => (UnityEngine.Object)(object)x != (UnityEngine.Object)null && ((Component)x).gameObject.activeInHierarchy && (string.Equals(((UnityEngine.Object)x).name, "Services", StringComparison.OrdinalIgnoreCase) || ((UnityEngine.Object)x).name.IndexOf("service", StringComparison.OrdinalIgnoreCase) >= 0));
		if ((UnityEngine.Object)(object)val2 != (UnityEngine.Object)null)
		{
			Selectable componentInParent = ((Component)val2).GetComponentInParent<Selectable>(true);
			if ((UnityEngine.Object)(object)componentInParent != (UnityEngine.Object)null)
			{
				return (Component?)(object)componentInParent;
			}
			return (Component?)(object)val2;
		}
		return null;
	}

	private static Selectable? FindServicesSelectableByScoring(Component root)
	{
		//IL_0101: Unknown result type (might be due to invalid IL or missing references)
		//IL_0106: Unknown result type (might be due to invalid IL or missing references)
		Selectable result = null;
		int num = int.MinValue;
		Selectable[] componentsInChildren = root.GetComponentsInChildren<Selectable>(true);
		foreach (Selectable val in componentsInChildren)
		{
			if ((UnityEngine.Object)(object)val == (UnityEngine.Object)null || !((Component)val).gameObject.activeInHierarchy)
			{
				continue;
			}
			int num2 = 0;
			string name = ((UnityEngine.Object)((Component)val).gameObject).name;
			if (name.IndexOf("service", StringComparison.OrdinalIgnoreCase) >= 0 || string.Equals(name, "Services", StringComparison.OrdinalIgnoreCase))
			{
				num2 += 50;
			}
			if ((UnityEngine.Object)(object)((Component)val).transform.parent != (UnityEngine.Object)null && (UnityEngine.Object)(object)((Component)((Component)val).transform.parent).GetComponent<HorizontalOrVerticalLayoutGroup>() != (UnityEngine.Object)null)
			{
				num2 += 10;
			}
			if (HasLabel(((Component)val).transform, "服务") || HasLabel(((Component)val).transform, "Services"))
			{
				num2 += 40;
			}
			if (HasAncestorName(((Component)val).transform, "Header"))
			{
				num2 -= 100;
			}
			RectTransform component = ((Component)val).GetComponent<RectTransform>();
			if ((UnityEngine.Object)(object)component != (UnityEngine.Object)null)
			{
				Rect rect = component.rect;
				if (rect.width < 10f || rect.height < 10f)
				{
					num2 -= 50;
				}
			}
			if (num2 > num)
			{
				num = num2;
				result = val;
			}
		}
		if (num >= 30)
		{
			return result;
		}
		return null;
	}

	private static bool HasAncestorName(Transform t, string needle)
	{
		Transform parent = t.parent;
		for (int i = 0; i < 12; i++)
		{
			if (!((UnityEngine.Object)(object)parent != (UnityEngine.Object)null))
			{
				break;
			}
			if (((UnityEngine.Object)parent).name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
			parent = parent.parent;
		}
		return false;
	}

	private static bool HasLabel(Transform t, string needle)
	{
		Text[] componentsInChildren = ((Component)t).GetComponentsInChildren<Text>(true);
		foreach (Text val in componentsInChildren)
		{
			if ((UnityEngine.Object)(object)val != (UnityEngine.Object)null && !string.IsNullOrWhiteSpace(val.text) && val.text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
		}
		Type type = TraderDealScreenVisitButton.FindType("TMPro.TMP_Text");
		if (type == null)
		{
			return false;
		}
		PropertyInfo property = type.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
		if (property == null)
		{
			return false;
		}
		MonoBehaviour[] componentsInChildren2 = ((Component)t).GetComponentsInChildren<MonoBehaviour>(true);
		foreach (MonoBehaviour val2 in componentsInChildren2)
		{
			if ((UnityEngine.Object)(object)val2 == (UnityEngine.Object)null || !type.IsAssignableFrom(((object)val2).GetType()))
			{
				continue;
			}
			try
			{
				string text = property.GetValue(val2, null) as string;
				if (!string.IsNullOrWhiteSpace(text) && text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return true;
				}
			}
			catch
			{
			}
		}
		return false;
	}

	private static string? TryExtractTraderId(object[] args)
	{
		object[] array = args;
		for (int i = 0; i < array.Length; i++)
		{
			if (array[i] is string text && LooksLikeTraderId(text))
			{
				return text;
			}
		}
		array = args;
		foreach (object obj in array)
		{
			if (obj == null)
			{
				continue;
			}
			Type type = obj.GetType();
			string[] array2 = new string[5] { "Id", "_id", "id", "TraderId", "traderId" };
			foreach (string name in array2)
			{
				PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (property != null && property.PropertyType == typeof(string) && property.GetValue(obj, null) is string text2 && LooksLikeTraderId(text2))
				{
					return text2;
				}
				FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (field != null && field.FieldType == typeof(string) && field.GetValue(obj) is string text3 && LooksLikeTraderId(text3))
				{
					return text3;
				}
			}
		}
		return null;
	}

	private static string? TryFindTraderIdOnInstance(object instance)
	{
		Type type = instance.GetType();
		string[] array = new string[6] { "TraderId", "_traderId", "traderId", "Id", "_id", "id" };
		foreach (string name in array)
		{
			PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (property != null && property.PropertyType == typeof(string) && property.GetValue(instance, null) is string text && LooksLikeTraderId(text))
			{
				return text;
			}
			FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field != null && field.FieldType == typeof(string) && field.GetValue(instance) is string text2 && LooksLikeTraderId(text2))
			{
				return text2;
			}
		}
		return null;
	}

	private static string? TryFindTraderIdDeep(object instance)
	{
		for (Type type = instance.GetType(); type != null; type = type.BaseType)
		{
			switch (type.Name)
			{
			default:
			{
				FieldInfo[] fields = type.GetFields(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				foreach (FieldInfo fieldInfo in fields)
				{
					if (fieldInfo.FieldType.IsPrimitive || fieldInfo.FieldType == typeof(string) || fieldInfo.FieldType.IsEnum)
					{
						continue;
					}
					object value;
					try
					{
						value = fieldInfo.GetValue(instance);
					}
					catch
					{
						continue;
					}
					if (value != null)
					{
						string text = TryFindTraderIdOnInstance(value);
						if (text != null)
						{
							return text;
						}
					}
				}
				continue;
			}
			case "MonoBehaviour":
			case "Behaviour":
			case "Component":
			case "UIInputNode":
			case "UIElement":
			case "UnityEngine.Object":
				break;
			}
			break;
		}
		return null;
	}

	private static bool LooksLikeTraderId(string s)
	{
		if (string.IsNullOrWhiteSpace(s))
		{
			return false;
		}
		if (s.Length == 24 && IsAllHex(s))
		{
			return true;
		}
		if (s.Length >= 4 && s.Length <= 64)
		{
			foreach (char c in s)
			{
				if (!char.IsLetterOrDigit(c) && c != '_' && c != '-')
				{
					return false;
				}
			}
			return true;
		}
		return false;
	}

	private static bool IsAllHex(string s)
	{
		foreach (char c in s)
		{
			if ((c < '0' || c > '9') && (c < 'a' || c > 'f') && (c < 'A' || c > 'F'))
			{
				return false;
			}
		}
		return true;
	}

	internal static void SetPluginActivatedTab(Component? tab)
	{
		//IL_0116: Unknown result type (might be due to invalid IL or missing references)
		//IL_0124: Expected O, but got Unknown
		_pluginActivatedTab = tab;
		VisitPlugin.Log.LogInfo((object)string.Format("[TabFix] SetPluginActivatedTab: {0} patched={1}", ((object)tab)?.GetType().Name ?? "null", _tabSelectPatched));
		if ((UnityEngine.Object)(object)tab == (UnityEngine.Object)null || _tabSelectPatched || _tabSelectHarmony == null)
		{
			return;
		}
		MethodInfo methodInfo = null;
		MethodInfo[] methods = ((object)tab).GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		foreach (MethodInfo methodInfo2 in methods)
		{
			if (methodInfo2.Name != "Select" || methodInfo2.IsAbstract)
			{
				continue;
			}
			ParameterInfo[] parameters = methodInfo2.GetParameters();
			if (parameters.Length == 0)
			{
				methodInfo = methodInfo2;
				break;
			}
			bool flag = true;
			ParameterInfo[] array = parameters;
			for (int j = 0; j < array.Length; j++)
			{
				if (!array[j].HasDefaultValue)
				{
					flag = false;
					break;
				}
			}
			if (flag)
			{
				methodInfo = methodInfo2;
				break;
			}
		}
		if (methodInfo == null)
		{
			VisitPlugin.Log.LogWarning((object)("[TabFix] Tab.Select() not found on " + ((object)tab).GetType().Name));
			return;
		}
		try
		{
			_tabSelectHarmony.Patch((MethodBase)methodInfo, new HarmonyMethod(typeof(TraderDealScreenHook), "TabSelectPrefix", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
			_tabSelectPatched = true;
			VisitPlugin.Log.LogInfo((object)$"[TabFix] Patched {((object)tab).GetType().Name}.{methodInfo.Name}({methodInfo.GetParameters().Length} params) for sibling-deselect");
		}
		catch (Exception ex)
		{
			VisitPlugin.Log.LogWarning((object)("[TabFix] Patch failed: " + ex.Message));
		}
	}

	private static void TabSelectPrefix(Component __instance)
	{
		Component pluginActivatedTab = _pluginActivatedTab;
		if ((UnityEngine.Object)(object)pluginActivatedTab == (UnityEngine.Object)null || __instance == pluginActivatedTab || (UnityEngine.Object)(object)__instance.transform.parent != (UnityEngine.Object)(object)pluginActivatedTab.transform.parent)
		{
			return;
		}
		_pluginActivatedTab = null;
		MethodInfo methodInfo = null;
		MethodInfo[] methods = ((object)pluginActivatedTab).GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		foreach (MethodInfo methodInfo2 in methods)
		{
			if (methodInfo2.Name != "Deselect" || methodInfo2.IsAbstract)
			{
				continue;
			}
			ParameterInfo[] parameters = methodInfo2.GetParameters();
			if (parameters.Length == 0)
			{
				methodInfo = methodInfo2;
				break;
			}
			bool flag = true;
			ParameterInfo[] array = parameters;
			for (int j = 0; j < array.Length; j++)
			{
				if (!array[j].HasDefaultValue)
				{
					flag = false;
					break;
				}
			}
			if (flag)
			{
				methodInfo = methodInfo2;
				break;
			}
		}
		try
		{
			if (methodInfo != null)
			{
				object[] parameters2 = ((methodInfo.GetParameters().Length != 0) ? new object[methodInfo.GetParameters().Length] : null);
				methodInfo.Invoke(pluginActivatedTab, parameters2);
			}
			VisitPlugin.Log.LogInfo((object)("[TabFix] Deselected plugin tab '" + ((UnityEngine.Object)pluginActivatedTab.gameObject).name + "' → '" + ((UnityEngine.Object)__instance.gameObject).name + "' selected"));
		}
		catch (Exception ex)
		{
			VisitPlugin.Log.LogWarning((object)("[TabFix] Deselect failed: " + ex.Message));
		}
	}
}
