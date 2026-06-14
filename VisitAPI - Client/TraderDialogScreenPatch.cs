using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VisitAPI;

internal static class TraderDialogScreenPatch
{
	internal static bool DialogSuppressed = true;

	private static readonly string[] VanillaIds = new string[4] { "638f541a29ffd1183d187f57", "656f0f98d80a697f855d34b1", "54cb50c76803fa8b248b4571", "54cb57776803fa99248b456e" };

	public static void TryPatch(Harmony harmony)
	{
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_006b: Expected O, but got Unknown
		//IL_00d9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e7: Expected O, but got Unknown
		Type type = AccessTools.TypeByName("EFT.UI.TraderDialogScreen");
		if (type == null)
		{
			VisitPlugin.Log.LogWarning((object)"TraderDialogScreenPatch: TraderDialogScreen not found");
			return;
		}
		MethodInfo method = type.GetMethod("method_5", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (method == null)
		{
			VisitPlugin.Log.LogWarning((object)"TraderDialogScreenPatch: method_5 not found");
			return;
		}
		harmony.Patch((MethodBase)method, new HarmonyMethod(typeof(TraderDialogScreenPatch), "Method5Prefix", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
		VisitPlugin.Log.LogInfo((object)"TraderDialogScreenPatch: method_5 patched");
		HashSet<MethodInfo> hashSet = new HashSet<MethodInfo>();
		Type type2 = type;
		while (type2 != null && type2 != typeof(object))
		{
			foreach (MethodInfo declaredMethod in AccessTools.GetDeclaredMethods(type2))
			{
				if (!(declaredMethod.Name != "Show") && !declaredMethod.IsAbstract && hashSet.Add(declaredMethod))
				{
					harmony.Patch((MethodBase)declaredMethod, new HarmonyMethod(typeof(TraderDialogScreenPatch), "ShowPrefix", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
				}
			}
			type2 = type2.BaseType;
		}
		VisitPlugin.Log.LogInfo((object)$"TraderDialogScreenPatch: {hashSet.Count} Show overload(s) patched");
	}

	private static bool ShowPrefix(object __instance, object[] __args)
	{
		if (!DialogSuppressed)
		{
			return true;
		}
		string text = TryGetTraderIdFromArg((__args != null && __args.Length != 0) ? __args[0] : null) ?? GetTraderId(__instance);
		if (text == null)
		{
			return true;
		}
		if (Array.IndexOf<string>(VanillaIds, text) >= 0)
		{
			return true;
		}
		return false;
	}

	private static string? TryGetTraderIdFromArg(object? arg)
	{
		if (arg == null)
		{
			return null;
		}
		FieldInfo[] fields = arg.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		foreach (FieldInfo fieldInfo in fields)
		{
			if (fieldInfo.FieldType != typeof(string))
			{
				continue;
			}
			try
			{
				string text = fieldInfo.GetValue(arg) as string;
				if (!string.IsNullOrEmpty(text) && LooksLikeTraderId(text))
				{
					return text;
				}
			}
			catch
			{
			}
		}
		fields = arg.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		foreach (FieldInfo fieldInfo2 in fields)
		{
			if (fieldInfo2.FieldType == typeof(string))
			{
				continue;
			}
			try
			{
				object value = fieldInfo2.GetValue(arg);
				if (value != null)
				{
					string text2 = value.GetType().GetMethod("op_Implicit", BindingFlags.Static | BindingFlags.Public, null, new Type[1] { value.GetType() }, null)?.Invoke(null, new object[1] { value }) as string;
					if (!string.IsNullOrEmpty(text2) && LooksLikeTraderId(text2))
					{
						return text2;
					}
				}
			}
			catch
			{
			}
		}
		return null;
	}

	private static bool LooksLikeTraderId(string? s)
	{
		if (s == null || s.Length != 24)
		{
			return false;
		}
		foreach (char c in s)
		{
			if ((c < '0' || c > '9') && (c < 'a' || c > 'f') && (c < 'A' || c > 'F'))
			{
				return false;
			}
		}
		return true;
	}

	private static bool Method5Prefix(object __instance)
	{
		string traderId = GetTraderId(__instance);
		if (traderId == null)
		{
			return true;
		}
		if (Array.IndexOf<string>(VanillaIds, traderId) >= 0)
		{
			return true;
		}
		if (DialogSuppressed)
		{
			return false;
		}
		try
		{
			RunMethod5WithoutWhitelist(__instance, traderId);
		}
		catch (Exception arg)
		{
			VisitPlugin.Log.LogError((object)$"TraderDialogScreenPatch.Method5Prefix: {arg}");
		}
		return false;
	}

	private static void RunMethod5WithoutWhitelist(object instance, string traderId)
	{
		Type type = instance.GetType();
		object dialogCtrl = FindFieldUp(instance, "dialogController");
		if (dialogCtrl == null)
		{
			VisitPlugin.Log.LogWarning((object)("method_5 bypass: dialogController not found on " + type.Name));
			return;
		}
		object obj = type.GetField("profile_0", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance);
		object obj2 = type.GetField("mongoID_0", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance);
		object traderInfo = null;
		if (obj != null && obj2 != null)
		{
			try
			{
				object obj3 = obj.GetType().GetProperty("TradersInfo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj);
				if (obj3 != null)
				{
					traderInfo = obj3.GetType().GetMethod("get_Item")?.Invoke(obj3, new object[1] { obj2 });
				}
			}
			catch (TargetInvocationException ex)
			{
				VisitPlugin.Log.LogWarning((object)("TradersInfo lookup for " + traderId + ": " + ex.InnerException?.Message));
			}
			catch (Exception ex2)
			{
				VisitPlugin.Log.LogWarning((object)("TradersInfo lookup error: " + ex2.Message));
			}
		}
		object obj4 = type.GetField("_dialogWindow", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance);
		if (obj4 != null && dialogCtrl != null && traderInfo != null)
		{
			MethodInfo methodInfo = FindMethod(obj4.GetType(), "Show", (ParameterInfo[] p) => p.Length == 2 && p[0].ParameterType.IsAssignableFrom(dialogCtrl.GetType()) && p[1].ParameterType.IsAssignableFrom(traderInfo.GetType()));
			if (methodInfo != null)
			{
				try
				{
					methodInfo.Invoke(obj4, new object[2] { dialogCtrl, traderInfo });
					VisitPlugin.Log.LogInfo((object)("_dialogWindow.Show called for " + traderId));
				}
				catch (Exception ex3)
				{
					VisitPlugin.Log.LogWarning((object)("_dialogWindow.Show: " + (ex3.InnerException?.Message ?? ex3.Message)));
				}
			}
			else
			{
				VisitPlugin.Log.LogWarning((object)"_dialogWindow.Show(GClass3619, TraderInfo) not found");
			}
		}
		ForceDialogWindowVisible(obj4, traderId, traderInfo);
	}

	private static void ForceDialogWindowVisible(object? dialogWindow, string traderId, object? traderInfo)
	{
		if (dialogWindow == null)
		{
			VisitPlugin.Log.LogWarning((object)"ForceDialogWindowVisible: _dialogWindow is null");
			return;
		}
		Type type = dialogWindow.GetType();
		try
		{
			type.GetMethod("ShowGameObject", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(dialogWindow, null);
		}
		catch (Exception ex)
		{
			VisitPlugin.Log.LogWarning((object)("ShowGameObject on dialogWindow: " + ex.Message));
		}
		if (traderInfo != null)
		{
			return;
		}
		try
		{
			object obj = type.GetField("_traderName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(dialogWindow);
			UnityEngine.Object val = (UnityEngine.Object)((obj is UnityEngine.Object) ? obj : null);
			if (val != null)
			{
				PropertyInfo property = ((object)val).GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
				string text = DialogTreeLoader.TryLoad(traderId)?.TraderName;
				property?.SetValue(val, string.IsNullOrEmpty(text) ? traderId : text);
			}
		}
		catch
		{
		}
	}

	private static string? GetTraderId(object instance)
	{
		object obj = instance.GetType().GetField("mongoID_0", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance);
		if (obj == null)
		{
			return null;
		}
		try
		{
			return obj.GetType().GetMethod("op_Implicit", BindingFlags.Static | BindingFlags.Public, null, new Type[1] { obj.GetType() }, null)?.Invoke(null, new object[1] { obj }) as string;
		}
		catch
		{
			return null;
		}
	}

	private static object? FindFieldUp(object instance, string name)
	{
		Type type = instance.GetType();
		while (type != null)
		{
			FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field != null)
			{
				return field.GetValue(instance);
			}
			type = type.BaseType;
		}
		return null;
	}

	private static MethodInfo? FindMethod(Type type, string name, Func<ParameterInfo[], bool> paramFilter)
	{
		while (type != null)
		{
			MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			foreach (MethodInfo methodInfo in methods)
			{
				if (methodInfo.Name == name && paramFilter(methodInfo.GetParameters()))
				{
					return methodInfo;
				}
			}
			type = type.BaseType;
		}
		return null;
	}
}

// ── Option-row pointer-event patch ────────────────────────────────────────────

internal static class TraderDialogWindowOptionRowPatch
{
	private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

	public static void TryPatch(Harmony harmony)
	{
		Type type = AccessTools.TypeByName("EFT.UI.TraderDialogWindowOptionRow");
		if (type == null)
		{
			VisitPlugin.Log.LogWarning((object)"TraderDialogWindowOptionRowPatch: type not found");
			return;
		}
		Patch(harmony, type, "OnPointerEnter", "Prefix_OnPointerEnter");
		Patch(harmony, type, "OnPointerClick", "Prefix_OnPointerClick");
		VisitPlugin.Log.LogInfo((object)"TraderDialogWindowOptionRowPatch: patched");
	}

	private static void Patch(Harmony harmony, Type type, string methodName, string prefixName)
	{
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Expected O, but got Unknown
		MethodInfo method = type.GetMethod(methodName, All);
		if (method == null)
		{
			VisitPlugin.Log.LogWarning((object)("TraderDialogWindowOptionRowPatch: " + methodName + " not found"));
			return;
		}
		harmony.Patch((MethodBase)method, new HarmonyMethod(typeof(TraderDialogWindowOptionRowPatch), prefixName, (Type[])null),
			(HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
	}

	private static bool IsOurRow(object instance)
	{
		FieldInfo field = instance.GetType().GetField("gclass3625_0", All);
		return field != null && field.GetValue(instance) == null;
	}

	private static bool Prefix_OnPointerEnter(object __instance)
	{
		if (!IsOurRow(__instance)) return true;
		MethodInfo method = __instance.GetType().GetMethod("method_1", All);
		if (method != null)
		{
			try { method.Invoke(__instance, new object[1] { true }); }
			catch { }
		}
		return false;
	}

	private static bool Prefix_OnPointerClick(object __instance)
	{
		if (!IsOurRow(__instance)) return true;
		object obj = ((__instance is MonoBehaviour) ? __instance : null);
		VisitApiInjectedOption? opt = ((obj != null) ? ((Component)obj).GetComponent<VisitApiInjectedOption>() : null);
		if (opt?.Callback != null)
		{
			try { opt.Callback(); }
			catch (Exception ex) { VisitPlugin.Log.LogError((object)$"RowClick callback: {ex}"); }
		}
		return false;
	}
}
