using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

namespace VisitAPI;

internal sealed class TraderDealScreenVisitButton : MonoBehaviour
{
	private static Type? _cachedClickTriggerType;

	private static bool _duringTabSwitch;

	internal static bool PluginActivatedTasksTab;

	private static string? _currentBgPath;

	private GameObject? _tabGo;

	private string? _lastTraderId;

	private Component? _servicesAnchor;

	private float _lastClickAt = -999f;

	private bool _clickInProgress;

	private static bool _dialogIsOpen;

	private static Type? _cachedTmpTextType;

	private static string _cachedPlayerName = "";

	private static string _cachedProfileId = "";

	private static object? _cachedProfile;

	internal static object? _cachedInvCtrl;

	internal static object? _cachedQuestCtrl;

	private static int _cachedTraderLevel = 1;

	private static double _cachedTraderStanding = 0.0;

	private static MethodInfo? _s_setIgnoreInputInNpcDialog;

	internal static Action? ActiveNativeClose { get; private set; }

	internal static bool TryGetCachedProfile(out string profileId, out string playerName)
	{
		profileId = _cachedProfileId;
		playerName = _cachedPlayerName;
		return !string.IsNullOrEmpty(profileId);
	}

	internal static void SetCachedProfile(string profileId, string playerName)
	{
		if (!string.IsNullOrEmpty(profileId))
		{
			_cachedProfileId = profileId;
			_cachedPlayerName = playerName;
		}
	}

	// 商人界面"拜访"页签门控：对话树配置了 tab 条件（如 SORA 的存储装置任务）时，
	// 仅在任务状态匹配时显示页签；未配置的商人不受影响
	private static bool IsVisitTabAllowed(string? traderId)
	{
		if (string.IsNullOrEmpty(traderId))
		{
			return false;
		}
		DialogTree? tree = DialogTreeLoader.TryLoad(traderId);
		if (tree == null || string.IsNullOrEmpty(tree.TabQuestId) || tree.TabShowWhenStatus == null || tree.TabShowWhenStatus.Count == 0)
		{
			return true;
		}
		if (!string.IsNullOrEmpty(_cachedProfileId))
		{
			QuestStatusCache.BatchFetch(_cachedProfileId, new string[1] { tree.TabQuestId });
		}
		return QuestStatusCache.AnyMatches(tree.TabShowWhenStatus, QuestStatusCache.GetStatus(tree.TabQuestId));
	}

	internal static void SetCachedControllers(object? profile, object? questCtrl, object? invCtrl)
	{
		if (profile != null)
		{
			_cachedProfile = profile;
		}
		if (questCtrl != null)
		{
			_cachedQuestCtrl = questCtrl;
		}
		if (invCtrl != null)
		{
			_cachedInvCtrl = invCtrl;
		}
	}

	public void Refresh(Component? servicesAnchor, string? traderId)
	{
		if (!string.IsNullOrEmpty(traderId))
		{
			_lastTraderId = traderId;
		}
		if ((UnityEngine.Object)(object)_servicesAnchor != (UnityEngine.Object)(object)servicesAnchor)
		{
			_servicesAnchor = servicesAnchor;
			if ((UnityEngine.Object)(object)_tabGo != (UnityEngine.Object)null)
			{
				UnityEngine.Object.Destroy((UnityEngine.Object)(object)_tabGo);
				_tabGo = null;
			}
		}
		EnsureUi(servicesAnchor);
		UpdateVisibility();
	}

	private void EnsureUi(Component? servicesAnchor)
	{
		//IL_01ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_01fb: Unknown result type (might be due to invalid IL or missing references)
		if ((UnityEngine.Object)(object)_tabGo != (UnityEngine.Object)null || !VisitPlugin.IsTraderRegistered(_lastTraderId) || !IsVisitTabAllowed(_lastTraderId))
		{
			return;
		}
		if ((UnityEngine.Object)(object)servicesAnchor == (UnityEngine.Object)null)
		{
			VisitPlugin.Log.LogWarning((object)"Services anchor is null; cannot place Visit next to it");
			return;
		}
		Transform parent = servicesAnchor.transform.parent;
		if ((UnityEngine.Object)(object)parent == (UnityEngine.Object)null)
		{
			VisitPlugin.Log.LogWarning((object)"Services anchor has no parent transform");
			return;
		}
		if (((UnityEngine.Object)parent).name.IndexOf("Header", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			VisitPlugin.Log.LogWarning((object)"Services anchor resolved to Header subtree; skip inject and wait next refresh");
			return;
		}
		_tabGo = UnityEngine.Object.Instantiate<GameObject>(servicesAnchor.gameObject, parent, false);
		((UnityEngine.Object)_tabGo).name = "VisitAPI.VisitTab";
		_tabGo.transform.SetSiblingIndex(servicesAnchor.transform.GetSiblingIndex() + 1);
		_tabGo.SetActive(true);
		Component component = _tabGo.GetComponent(((object)servicesAnchor).GetType());
		if ((UnityEngine.Object)(object)component != (UnityEngine.Object)null)
		{
			UnityEngine.Object.DestroyImmediate((UnityEngine.Object)(object)component);
		}
		SetAnyLabel(_tabGo, "拜访");
		WireClickHandler(_tabGo);
		FixLayoutWidth(_tabGo, servicesAnchor);
		Selectable val = (Selectable)(_tabGo.GetComponentInChildren<Toggle>(true) ?? ((object)_tabGo.GetComponentInChildren<Button>(true)) ?? ((object)_tabGo.GetComponentInChildren<Selectable>(true)));
		if ((UnityEngine.Object)(object)val != (UnityEngine.Object)null)
		{
			val.interactable = true;
		}
		VisitPlugin.Log.LogInfo((object)("Visit tab injected next to '" + ((UnityEngine.Object)servicesAnchor.gameObject).name + "'"));
		try
		{
			RectTransform val2 = (RectTransform)(object)((parent is RectTransform) ? parent : null);
			if ((UnityEngine.Object)(object)val2 != (UnityEngine.Object)null)
			{
				Canvas.ForceUpdateCanvases();
				LayoutRebuilder.ForceRebuildLayoutImmediate(val2);
			}
		}
		catch
		{
		}
		try
		{
			RectTransform component2 = _tabGo.GetComponent<RectTransform>();
			if ((UnityEngine.Object)(object)component2 != (UnityEngine.Object)null)
			{
				VisitPlugin.Log.LogInfo((object)$"Visit tab rect: activeInHierarchy={_tabGo.activeInHierarchy} sibling={_tabGo.transform.GetSiblingIndex()} pos={component2.anchoredPosition} size={component2.sizeDelta}");
			}
		}
		catch
		{
		}
	}

	private void WireClickHandler(GameObject go)
	{
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0061: Unknown result type (might be due to invalid IL or missing references)
		//IL_0067: Expected O, but got Unknown
		Toggle componentInChildren = go.GetComponentInChildren<Toggle>(true);
		if ((UnityEngine.Object)(object)componentInChildren != (UnityEngine.Object)null)
		{
			componentInChildren.group = null;
			((UnityEventBase)componentInChildren.onValueChanged).RemoveAllListeners();
		}
		Button componentInChildren2 = go.GetComponentInChildren<Button>(true);
		if ((UnityEngine.Object)(object)componentInChildren2 != (UnityEngine.Object)null)
		{
			((UnityEventBase)componentInChildren2.onClick).RemoveAllListeners();
		}
		EventTrigger obj = go.GetComponent<EventTrigger>() ?? go.AddComponent<EventTrigger>();
		obj.triggers.Clear();
		EventTrigger.Entry val = new EventTrigger.Entry
		{
			eventID = (EventTriggerType)4
		};
		((UnityEvent<BaseEventData>)(object)val.callback).AddListener((UnityAction<BaseEventData>)delegate
		{
			OnClick();
		});
		obj.triggers.Add(val);
		VisitPlugin.Log.LogInfo((object)"Visit tab: click wired via EventTrigger, Toggle detached from group");
	}

	private void OnClick()
	{
		if (_duringTabSwitch)
		{
			return;
		}
		float unscaledTime = Time.unscaledTime;
		if (unscaledTime - _lastClickAt < 0.25f)
		{
			return;
		}
		_lastClickAt = unscaledTime;
		if (_clickInProgress)
		{
			return;
		}
		_clickInProgress = true;
		try
		{
			string lastTraderId = _lastTraderId;
			if (!string.IsNullOrEmpty(lastTraderId))
			{
				string absolutePath = "";
				VisitPlugin.TryGetVisitPngForTrader(lastTraderId, out absolutePath);
				TraderDialogScreenPatch.DialogSuppressed = false;
				if (TryShowNativeDialogScreen(lastTraderId, absolutePath))
				{
					SelectVisitTab();
				}
				else
				{
					VisitPlugin.Log.LogWarning((object)("Native TraderDialogScreen failed for " + lastTraderId + "; dialog unavailable"));
				}
				TraderDialogScreenPatch.DialogSuppressed = true;
			}
		}
		finally
		{
			_clickInProgress = false;
		}
	}

	private static bool TryShowNativeDialogScreen(string traderId, string bgPath)
	{
		//IL_04ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_04d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_0513: Unknown result type (might be due to invalid IL or missing references)
		//IL_0518: Unknown result type (might be due to invalid IL or missing references)
		Action action = null;
		Component screensGroup = TraderDealScreenHook.ScreensGroup;
		if ((UnityEngine.Object)(object)screensGroup == (UnityEngine.Object)null)
		{
			return false;
		}
		object memberValue = GetMemberValue(screensGroup, "Profile_0", "Profile");
		object memberValue2 = GetMemberValue(screensGroup, "AbstractQuestControllerClass");
		object memberValue3 = GetMemberValue(screensGroup, "InventoryController_0", "InventoryController");
		if (memberValue == null || memberValue2 == null || memberValue3 == null)
		{
			VisitPlugin.Log.LogWarning((object)$"TraderScreensGroup data missing: Profile={memberValue != null}, Quest={memberValue2 != null}, Inv={memberValue3 != null}");
			return false;
		}
		_cachedQuestCtrl = memberValue2;
		_cachedInvCtrl = memberValue3;
		_cachedPlayerName = "";
		_cachedProfileId = "";
		try
		{
			Type type = memberValue.GetType();
			_cachedProfileId = ((type.GetProperty("Id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(memberValue) ?? type.GetField("Id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(memberValue)) as string) ?? "";
			object obj = type.GetProperty("Info", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(memberValue) ?? type.GetField("Info", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(memberValue) ?? type.GetField("info", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(memberValue);
			if (obj != null)
			{
				_cachedPlayerName = ((obj.GetType().GetProperty("Nickname", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj) ?? obj.GetType().GetField("Nickname", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj)) as string) ?? "";
			}
			if (string.IsNullOrEmpty(_cachedPlayerName))
			{
				_cachedPlayerName = ((type.GetProperty("Nickname", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(memberValue) ?? type.GetField("Nickname", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(memberValue)) as string) ?? "";
			}
			if (string.IsNullOrEmpty(_cachedPlayerName))
			{
				object obj2 = type.GetProperty("Characters", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(memberValue) ?? type.GetField("Characters", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(memberValue);
				object obj3 = obj2?.GetType().GetProperty("Pmc", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj2) ?? obj2?.GetType().GetField("Pmc", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj2);
				object obj4 = obj3?.GetType().GetProperty("Info", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj3) ?? obj3?.GetType().GetField("Info", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj3);
				_cachedPlayerName = ((obj4?.GetType().GetProperty("Nickname", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj4) ?? obj4?.GetType().GetField("Nickname", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj4)) as string) ?? "";
			}
			VisitPlugin.Log.LogInfo((object)("Player: name='" + _cachedPlayerName + "' id='" + _cachedProfileId + "'"));
		}
		catch (Exception ex)
		{
			VisitPlugin.Log.LogWarning((object)("Player name extraction: " + ex.Message));
		}
		TryExtractTraderLoyalty(memberValue, traderId);
		Type type2 = FindType("GClass3619") ?? FindType("GClass3618");
		if (type2 == null)
		{
			VisitPlugin.Log.LogWarning((object)"GClass3619/GClass3618 not found");
			return false;
		}
		object obj5;
		try
		{
			obj5 = Activator.CreateInstance(type2, memberValue, memberValue2, memberValue3);
		}
		catch (Exception ex2)
		{
			VisitPlugin.Log.LogError((object)("dialogController ctor (" + type2.Name + "): " + ex2.Message));
			return false;
		}
		MonoBehaviour[] array = Resources.FindObjectsOfTypeAll<MonoBehaviour>();
		foreach (MonoBehaviour mb in array)
		{
			if ((UnityEngine.Object)(object)mb == (UnityEngine.Object)null || ((object)mb).GetType().Name != "TraderDialogScreen")
			{
				continue;
			}
			Scene scene = ((Component)mb).gameObject.scene;
			if (!scene.IsValid())
			{
				VisitPlugin.Log.LogInfo((object)"TraderDialogScreen: skipping prefab asset");
				continue;
			}
			ManualLogSource log = VisitPlugin.Log;
			string[] obj6 = new string[6] { "TraderDialogScreen: scene=", null, null, null, null, null };
			scene = ((Component)mb).gameObject.scene;
			obj6[1] = scene.name;
			obj6[2] = " path=";
			obj6[3] = GetTransformPath(((Component)mb).transform);
			obj6[4] = " ";
			obj6[5] = $"activeSelf={((Component)mb).gameObject.activeSelf} activeInHierarchy={((Component)mb).gameObject.activeInHierarchy}";
			log.LogInfo((object)string.Concat(obj6));
			Type nestedType = ((object)mb).GetType().GetNestedType("BTRDialogClass", BindingFlags.Public | BindingFlags.NonPublic);
			if (nestedType == null)
			{
				continue;
			}
			object obj7;
			try
			{
				obj7 = Activator.CreateInstance(nestedType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[7] { memberValue, traderId, memberValue2, memberValue3, null, obj5, null }, null);
			}
			catch (Exception ex3)
			{
				VisitPlugin.Log.LogWarning((object)("BTRDialogClass ctor: " + ex3.Message));
				continue;
			}
			MethodInfo methodInfo = Array.Find(((object)mb).GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic), (MethodInfo m) => m.Name == "Show" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.Name == "BTRDialogClass");
			if (methodInfo == null)
			{
				continue;
			}
			try
			{
				ActivateParentChain(((Component)mb).transform);
				((Component)mb).gameObject.SetActive(true);
				Canvas val = ((Component)mb).GetComponent<Canvas>();
				if ((UnityEngine.Object)(object)val == (UnityEngine.Object)null)
				{
					val = ((Component)mb).gameObject.AddComponent<Canvas>();
					if ((UnityEngine.Object)(object)((Component)mb).GetComponent<GraphicRaycaster>() == (UnityEngine.Object)null)
					{
						((Component)mb).gameObject.AddComponent<GraphicRaycaster>();
					}
				}
				val.overrideSorting = true;
				val.sortingOrder = 999;
				VisitPlugin.Log.LogInfo((object)"Canvas override: overrideSorting=true sortingOrder=999");
				methodInfo.Invoke(mb, new object[1] { obj7 });
				VisitPlugin.Log.LogInfo((object)("Native TraderDialogScreen shown for " + traderId));
				DialogTree dialogTree = DialogTreeLoader.TryLoad(traderId);
				if (dialogTree != null)
				{
					string text;
					if (!string.IsNullOrEmpty(dialogTree.FirstVisitNode) && DialogStateStore.IsFirstVisit(traderId, _cachedProfileId) && dialogTree.FirstVisitTrigger == null)
					{
						DialogStateStore.MarkVisited(traderId, _cachedProfileId);
						text = dialogTree.FirstVisitNode;
					}
					else
					{
						string text2 = RandomDialogStore.ConsumePending(traderId);
						text = ((text2 != null && dialogTree.Nodes.ContainsKey(text2)) ? text2 : ResolveStartNode(dialogTree));
					}
					if (dialogTree.Nodes.TryGetValue(text, out DialogNode value) && !string.IsNullOrEmpty(value.Background))
					{
						string text3 = DialogTreeLoader.ResolvePath(value.Background);
						if (text3 != null && File.Exists(text3))
						{
							bgPath = text3;
						}
					}
					if (!string.IsNullOrEmpty(bgPath))
					{
						InjectBackgroundIntoDialogScreen(mb, bgPath);
					}
					mb.StartCoroutine(InjectAfterDelay(mb, dialogTree, text));
				}
				else if (!InjectMinimalOptions(mb, "没有别的事情了。"))
				{
					VisitPlugin.Log.LogWarning((object)("Native dialog has no options for " + traderId));
				}
				action = (ActiveNativeClose = delegate
				{
					ActiveNativeClose = null;
					DeselectCurrentVisitTab();
					CleanupDialogVisuals(mb);
					CloseNativeDialog(mb);
				});
				(((Component)mb).GetComponent<VisitApiEscHandler>() ?? ((Component)mb).gameObject.AddComponent<VisitApiEscHandler>()).CloseAction = action;
				return true;
			}
			catch (TargetInvocationException ex4)
			{
				Exception ex5 = ex4.InnerException ?? ex4;
				VisitPlugin.Log.LogError((object)ex5);
				if ((ex5.Message ?? "").IndexOf("Unable to find trader controller", StringComparison.OrdinalIgnoreCase) >= 0 && InjectMinimalOptions(mb, "我没有别的事情了。"))
				{
					action = delegate
					{
						DeselectCurrentVisitTab();
						CleanupDialogVisuals(mb);
						CloseNativeDialog(mb);
					};
					VisitPlugin.Log.LogInfo((object)"Native dialog controller missing; injected minimal options");
					return true;
				}
			}
			catch (Exception ex6)
			{
				VisitPlugin.Log.LogError((object)ex6);
			}
		}
		VisitPlugin.Log.LogWarning((object)"TraderDialogScreen scene instance not found");
		return false;
	}

	internal static bool TryShowNativeDialogInRaid(string traderId, DialogTree tree, string startNode, string profileId, string playerName, Action? onClose)
	{
		//IL_0160: Unknown result type (might be due to invalid IL or missing references)
		//IL_0165: Unknown result type (might be due to invalid IL or missing references)
		//IL_0230: Unknown result type (might be due to invalid IL or missing references)
		//IL_0235: Unknown result type (might be due to invalid IL or missing references)
		Action onClose2 = onClose;
		object obj = _cachedProfile;
		object obj2 = _cachedQuestCtrl;
		object obj3 = _cachedInvCtrl;
		if ((obj == null || obj2 == null || obj3 == null) && VisitPlugin.TryGetInRaidControllers(out object profile, out object questCtrl, out object invCtrl))
		{
			if (obj == null)
			{
				obj = profile;
			}
			if (obj2 == null)
			{
				obj2 = questCtrl;
			}
			if (obj3 == null)
			{
				obj3 = invCtrl;
			}
		}
		if (obj == null)
		{
			VisitPlugin.Log.LogWarning((object)$"[InRaidDialog] No player profile available (quest={obj2 != null} inv={obj3 != null})");
			return false;
		}
		_cachedProfileId = profileId;
		_cachedPlayerName = playerName;
		Type type = FindType("GClass3619") ?? FindType("GClass3618");
		if (type == null)
		{
			VisitPlugin.Log.LogWarning((object)"[InRaidDialog] GClass3619/3618 not found");
			return false;
		}
		object obj4;
		try
		{
			obj4 = Activator.CreateInstance(type, obj, obj2, obj3);
		}
		catch (Exception ex)
		{
			VisitPlugin.Log.LogError((object)("[InRaidDialog] dialogCtrl ctor: " + ex.Message));
			return false;
		}
		MonoBehaviour[] array = Resources.FindObjectsOfTypeAll<MonoBehaviour>();
		foreach (MonoBehaviour mb in array)
		{
			if ((UnityEngine.Object)(object)mb == (UnityEngine.Object)null || ((object)mb).GetType().Name != "TraderDialogScreen")
			{
				continue;
			}
			Scene scene = ((Component)mb).gameObject.scene;
			if (!scene.IsValid())
			{
				continue;
			}
			Type nestedType = ((object)mb).GetType().GetNestedType("BTRDialogClass", BindingFlags.Public | BindingFlags.NonPublic);
			if (nestedType == null)
			{
				continue;
			}
			object obj5;
			try
			{
				obj5 = Activator.CreateInstance(nestedType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[7] { obj, traderId, obj2, obj3, null, obj4, null }, null);
			}
			catch (Exception ex2)
			{
				VisitPlugin.Log.LogWarning((object)("[InRaidDialog] BTRDialogClass ctor: " + ex2.Message));
				continue;
			}
			MethodInfo methodInfo = Array.Find(((object)mb).GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic), (MethodInfo m) => m.Name == "Show" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.Name == "BTRDialogClass");
			if (methodInfo == null)
			{
				continue;
			}
			CursorLockMode savedLock;
			bool savedVisible;
			try
			{
				savedLock = Cursor.lockState;
				savedVisible = Cursor.visible;
				Cursor.lockState = (CursorLockMode)0;
				Cursor.visible = true;
				ActivateParentChain(((Component)mb).transform);
				((Component)mb).gameObject.SetActive(true);
				Canvas obj6 = ((Component)mb).GetComponent<Canvas>() ?? ((Component)mb).gameObject.AddComponent<Canvas>();
				obj6.overrideSorting = true;
				obj6.sortingOrder = 3000;
				if ((UnityEngine.Object)(object)((Component)mb).GetComponent<GraphicRaycaster>() == (UnityEngine.Object)null)
				{
					((Component)mb).gameObject.AddComponent<GraphicRaycaster>();
				}
				TryExtractTraderLoyalty(obj, traderId);
				TraderDialogScreenPatch.DialogSuppressed = false;
				try
				{
					methodInfo.Invoke(mb, new object[1] { obj5 });
				}
				finally
				{
					TraderDialogScreenPatch.DialogSuppressed = true;
				}
				mb.StartCoroutine(InjectAfterDelay(mb, tree, startNode));
				string absolutePath = "";
				if (tree.Nodes.TryGetValue(startNode, out DialogNode value) && !string.IsNullOrEmpty(value.Background))
				{
					string text = DialogTreeLoader.ResolvePath(value.Background);
					if (text != null && File.Exists(text))
					{
						absolutePath = text;
					}
				}
				if (string.IsNullOrEmpty(absolutePath))
				{
					VisitPlugin.TryGetVisitPngForTrader(traderId, out absolutePath);
				}
				if (!string.IsNullOrEmpty(absolutePath))
				{
					InjectBackgroundIntoDialogScreen(mb, absolutePath);
				}
				SetIgnoreInputInNPCDialogReflection(ignore: true);
				(((Component)mb).GetComponent<VisitApiEscHandler>() ?? ((Component)mb).gameObject.AddComponent<VisitApiEscHandler>()).CloseAction = CloseAction;
				VisitPlugin.Log.LogInfo((object)("[InRaidDialog] Native dialog shown for " + traderId + " → '" + startNode + "'"));
				return true;
			}
			catch (TargetInvocationException ex3)
			{
				VisitPlugin.Log.LogError((object)(ex3.InnerException ?? ex3));
			}
			catch (Exception ex4)
			{
				VisitPlugin.Log.LogError((object)ex4);
			}
			void CloseAction()
			{
				//IL_0007: Unknown result type (might be due to invalid IL or missing references)
				SetIgnoreInputInNPCDialogReflection(ignore: false);
				Cursor.lockState = savedLock;
				Cursor.visible = savedVisible;
				CleanupDialogVisuals(mb);
				CloseNativeDialog(mb);
				onClose2?.Invoke();
			}
		}
		VisitPlugin.Log.LogWarning((object)"[InRaidDialog] TraderDialogScreen not found in scene");
		return false;
	}

	private static void CloseNativeDialog(MonoBehaviour screen)
	{
		try
		{
			Type type = ((object)screen).GetType();
			MethodInfo methodInfo = type.GetMethod("HideGameObject", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? type.GetMethod("Hide", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? type.GetMethod("Close", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (methodInfo != null && methodInfo.GetParameters().Length == 0)
			{
				methodInfo.Invoke(screen, Array.Empty<object>());
			}
		}
		catch
		{
		}
		try
		{
			((Component)screen).gameObject.SetActive(false);
		}
		catch
		{
		}
	}

	private static bool InjectMinimalOptions(MonoBehaviour screen, string exitText)
	{
		MonoBehaviour screen2 = screen;
		try
		{
			RemoveAllExitOptions(((Component)screen2).transform, exitText);
			RemoveOverlayExit(((Component)screen2).transform);
			object dialogWindow = GetDialogWindow(screen2);
			Type type = dialogWindow?.GetType();
			Transform val = ((type != null) ? GetLinesContainerTransform(dialogWindow, type) : null);
			MethodInfo methodInfo = ((type != null) ? FindAddLineMethod(type) : null);
			VisitPlugin.Log.LogInfo((object)("InjectMinimalOptions: dw=" + (type?.Name ?? "null") + " lines=" + (((val != null) ? ((UnityEngine.Object)val).name : null) ?? "null") + " addLine=" + (methodInfo?.Name ?? "null")));
			if ((UnityEngine.Object)(object)val != (UnityEngine.Object)null)
			{
				ActivateToRoot(val, ((Component)screen2).transform);
			}
			Action callback = delegate
			{
				DeselectCurrentVisitTab();
				CleanupDialogVisuals(screen2);
				CloseNativeDialog(screen2);
			};
			Type ctType = FindType("EFT.UI.ClickTrigger") ?? FindType("ClickTrigger");
			Transform val2 = null;
			if (dialogWindow != null && methodInfo != null)
			{
				val2 = TryNativeAddLine(dialogWindow, methodInfo, val, exitText, callback);
			}
			if ((UnityEngine.Object)(object)val2 != (UnityEngine.Object)null)
			{
				((Component)val2).gameObject.AddComponent<VisitApiInjectedOption>().Callback = callback;
				ResetRowVisualState(val2);
				ForceEnableNativeDialogInteraction(screen2);
				return true;
			}
			Component val3 = (Component)((dialogWindow is Component) ? dialogWindow : null);
			Transform root = ((val3 != null) ? val3.transform : ((Component)screen2).transform);
			Transform val4 = FindOptionsContainer(root) ?? GetOrCreateOverlayContainer(root);
			ActivateToRoot(val4, ((Component)screen2).transform);
			Transform val5 = FindOptionTemplate(((Component)screen2).transform, val4);
			if ((UnityEngine.Object)(object)val5 == (UnityEngine.Object)null)
			{
				GameObject obj = BuildOverlayRow(val4);
				obj.AddComponent<VisitApiInjectedOption>();
				SetAnyLabel(obj, exitText);
				WireRowClick(obj.transform, screen2, callback);
				ForceEnableNativeDialogInteraction(screen2);
				return true;
			}
			GameObject obj2 = UnityEngine.Object.Instantiate<GameObject>(((Component)val5).gameObject, val4, false);
			((UnityEngine.Object)obj2).name = "VisitAPI.ExitOption";
			obj2.SetActive(true);
			obj2.AddComponent<VisitApiInjectedOption>();
			StripEftHandlers(obj2.transform, ctType);
			SetAnyLabel(obj2, exitText);
			WireRowClick(obj2.transform, screen2, callback);
			ForceEnableNativeDialogInteraction(screen2);
			return true;
		}
		catch (Exception ex)
		{
			VisitPlugin.Log.LogError((object)ex);
			return false;
		}
	}

	private static void RemoveAllExitOptions(Transform root, string exitText)
	{
		VisitApiInjectedOption[] componentsInChildren = ((Component)root).GetComponentsInChildren<VisitApiInjectedOption>(true);
		foreach (VisitApiInjectedOption visitApiInjectedOption in componentsInChildren)
		{
			if ((UnityEngine.Object)(object)visitApiInjectedOption != (UnityEngine.Object)null)
			{
				((Component)visitApiInjectedOption).gameObject.SetActive(false);
				UnityEngine.Object.Destroy((UnityEngine.Object)(object)((Component)visitApiInjectedOption).gameObject);
			}
		}
		foreach (Transform item in FindOptionRowsByLabel(root, exitText).Distinct())
		{
			if ((UnityEngine.Object)(object)item != (UnityEngine.Object)null && (UnityEngine.Object)(object)((Component)item).gameObject != (UnityEngine.Object)null)
			{
				((Component)item).gameObject.SetActive(false);
				UnityEngine.Object.Destroy((UnityEngine.Object)(object)((Component)item).gameObject);
			}
		}
	}

	private static void RemoveOverlayExit(Transform root)
	{
		try
		{
			Transform val = root.Find("VisitAPI.ExitOverlay");
			if ((UnityEngine.Object)(object)val != (UnityEngine.Object)null)
			{
				((Component)val).gameObject.SetActive(false);
				UnityEngine.Object.Destroy((UnityEngine.Object)(object)((Component)val).gameObject);
			}
		}
		catch
		{
		}
	}

	private static Transform GetOrCreateOverlayContainer(Transform root)
	{
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_0069: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0092: Unknown result type (might be due to invalid IL or missing references)
		//IL_009c: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b1: Expected O, but got Unknown
		//IL_00de: Unknown result type (might be due to invalid IL or missing references)
		Transform val = root.Find("VisitAPI.ExitOverlay");
		if ((UnityEngine.Object)(object)val != (UnityEngine.Object)null)
		{
			return val;
		}
		GameObject val2 = new GameObject("VisitAPI.ExitOverlay");
		val2.transform.SetParent(root, false);
		RectTransform obj = val2.AddComponent<RectTransform>();
		obj.anchorMin = new Vector2(0.5f, 0f);
		obj.anchorMax = new Vector2(0.5f, 0f);
		obj.pivot = new Vector2(0.5f, 0f);
		obj.anchoredPosition = new Vector2(0f, 18f);
		obj.sizeDelta = new Vector2(1100f, 56f);
		VerticalLayoutGroup obj2 = val2.AddComponent<VerticalLayoutGroup>();
		((LayoutGroup)obj2).padding = new RectOffset(0, 0, 0, 0);
		((HorizontalOrVerticalLayoutGroup)obj2).spacing = 0f;
		((LayoutGroup)obj2).childAlignment = (TextAnchor)7;
		((HorizontalOrVerticalLayoutGroup)obj2).childControlHeight = true;
		((HorizontalOrVerticalLayoutGroup)obj2).childControlWidth = true;
		((HorizontalOrVerticalLayoutGroup)obj2).childForceExpandHeight = false;
		((HorizontalOrVerticalLayoutGroup)obj2).childForceExpandWidth = true;
		ContentSizeFitter obj3 = val2.AddComponent<ContentSizeFitter>();
		obj3.horizontalFit = (ContentSizeFitter.FitMode)0;
		obj3.verticalFit = (ContentSizeFitter.FitMode)2;
		return val2.transform;
	}

	private static GameObject BuildOverlayRow(Transform parent)
	{
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Expected O, but got Unknown
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_006d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ed: Expected O, but got Unknown
		//IL_0125: Unknown result type (might be due to invalid IL or missing references)
		//IL_0147: Unknown result type (might be due to invalid IL or missing references)
		//IL_0152: Unknown result type (might be due to invalid IL or missing references)
		//IL_015d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0167: Unknown result type (might be due to invalid IL or missing references)
		//IL_0176: Unknown result type (might be due to invalid IL or missing references)
		//IL_017c: Expected O, but got Unknown
		//IL_02f8: Unknown result type (might be due to invalid IL or missing references)
		//IL_0334: Unknown result type (might be due to invalid IL or missing references)
		//IL_0340: Unknown result type (might be due to invalid IL or missing references)
		//IL_0356: Unknown result type (might be due to invalid IL or missing references)
		//IL_036c: Unknown result type (might be due to invalid IL or missing references)
		//IL_026b: Unknown result type (might be due to invalid IL or missing references)
		//IL_03c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_03cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_03e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_03ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_0406: Unknown result type (might be due to invalid IL or missing references)
		//IL_040b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0425: Unknown result type (might be due to invalid IL or missing references)
		//IL_042a: Unknown result type (might be due to invalid IL or missing references)
		//IL_042f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0434: Unknown result type (might be due to invalid IL or missing references)
		//IL_0436: Unknown result type (might be due to invalid IL or missing references)
		//IL_043d: Expected O, but got Unknown
		//IL_0462: Unknown result type (might be due to invalid IL or missing references)
		//IL_0467: Unknown result type (might be due to invalid IL or missing references)
		//IL_0469: Unknown result type (might be due to invalid IL or missing references)
		//IL_0470: Expected O, but got Unknown
		GameObject val = new GameObject("VisitAPI.ExitOption");
		val.transform.SetParent(parent, false);
		RectTransform obj = val.AddComponent<RectTransform>();
		obj.anchorMin = new Vector2(0f, 0f);
		obj.anchorMax = new Vector2(1f, 0f);
		obj.pivot = new Vector2(0.5f, 0f);
		obj.sizeDelta = new Vector2(0f, 44f);
		LayoutElement obj2 = val.AddComponent<LayoutElement>();
		obj2.preferredHeight = 44f;
		obj2.flexibleWidth = 1f;
		Image obj3 = val.AddComponent<Image>();
		((Graphic)obj3).color = new Color(0f, 0f, 0f, 0.02f);
		((Graphic)obj3).raycastTarget = true;
		CanvasGroup obj4 = val.AddComponent<CanvasGroup>();
		obj4.alpha = 1f;
		obj4.interactable = true;
		obj4.blocksRaycasts = true;
		obj4.ignoreParentGroups = true;
		GameObject val2 = new GameObject("Highlight");
		val2.transform.SetParent(val.transform, false);
		Image hiImg = val2.AddComponent<Image>();
		((Graphic)hiImg).color = new Color(0f, 0f, 0f, 0f);
		((Graphic)hiImg).raycastTarget = false;
		RectTransform rectTransform = ((Graphic)hiImg).rectTransform;
		rectTransform.anchorMin = Vector2.zero;
		rectTransform.anchorMax = Vector2.one;
		rectTransform.offsetMin = Vector2.zero;
		rectTransform.offsetMax = Vector2.zero;
		GameObject val3 = new GameObject("Text");
		val3.transform.SetParent(val.transform, false);
		Type type = FindType("TMPro.TextMeshProUGUI");
		Type type2 = FindType("TMPro.TMP_Text");
		RectTransform val4;
		if (type != null && type2 != null)
		{
			Component obj5 = val3.AddComponent(type);
			PropertyInfo property = type2.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
			PropertyInfo property2 = type2.GetProperty("fontSize", BindingFlags.Instance | BindingFlags.Public);
			PropertyInfo property3 = type2.GetProperty("color", BindingFlags.Instance | BindingFlags.Public);
			PropertyInfo property4 = type2.GetProperty("font", BindingFlags.Instance | BindingFlags.Public);
			PropertyInfo property5 = type2.GetProperty("alignment", BindingFlags.Instance | BindingFlags.Public);
			try
			{
				property?.SetValue(obj5, "");
			}
			catch
			{
			}
			try
			{
				property2?.SetValue(obj5, 18f);
			}
			catch
			{
			}
			try
			{
				property3?.SetValue(obj5, (object)new Color(0.94f, 0.91f, 0.8f, 1f));
			}
			catch
			{
			}
			object obj9 = FindSceneTmpFont(parent);
			if (obj9 != null)
			{
				try
				{
					property4?.SetValue(obj5, obj9);
				}
				catch
				{
				}
			}
			if (property5 != null)
			{
				try
				{
					property5.SetValue(obj5, Enum.ToObject(property5.PropertyType, 257));
				}
				catch
				{
				}
			}
			val4 = val3.GetComponent<RectTransform>();
		}
		else
		{
			Text obj12 = val3.AddComponent<Text>();
			obj12.text = "";
			((Graphic)obj12).color = new Color(0.94f, 0.91f, 0.8f, 1f);
			obj12.fontSize = 18;
			obj12.alignment = (TextAnchor)3;
			obj12.font = FindEftFont(parent) ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
			val4 = ((Graphic)obj12).rectTransform;
		}
		val4.anchorMin = Vector2.zero;
		val4.anchorMax = Vector2.one;
		val4.offsetMin = new Vector2(16f, 0f);
		val4.offsetMax = new Vector2(-16f, 0f);
		Component textComp = (Component)(((object)val3.GetComponent<MonoBehaviour>()) ?? ((object)val3.GetComponent<Text>()));
		PropertyInfo textColorProp = ((object)textComp)?.GetType().GetProperty("color", BindingFlags.Instance | BindingFlags.Public);
		EventTrigger obj13 = val.AddComponent<EventTrigger>();
		obj13.triggers = new List<EventTrigger.Entry>();
		Color normalBg = ((Graphic)hiImg).color;
		Color hoverBg = new Color(1f, 1f, 1f, 0.1f);
		Color normalTextColor = new Color(0.94f, 0.91f, 0.8f, 1f);
		Color hoverTextColor = new Color(1f, 1f, 1f, 1f);
		EventTrigger.Entry val5 = new EventTrigger.Entry
		{
			eventID = (EventTriggerType)0
		};
		((UnityEvent<BaseEventData>)(object)val5.callback).AddListener((UnityAction<BaseEventData>)delegate
		{
			//IL_0007: Unknown result type (might be due to invalid IL or missing references)
			//IL_0024: Unknown result type (might be due to invalid IL or missing references)
			((Graphic)hiImg).color = hoverBg;
			try
			{
				textColorProp?.SetValue(textComp, hoverTextColor);
			}
			catch
			{
			}
		});
		obj13.triggers.Add(val5);
		EventTrigger.Entry val6 = new EventTrigger.Entry
		{
			eventID = (EventTriggerType)1
		};
		((UnityEvent<BaseEventData>)(object)val6.callback).AddListener((UnityAction<BaseEventData>)delegate
		{
			//IL_0007: Unknown result type (might be due to invalid IL or missing references)
			//IL_0024: Unknown result type (might be due to invalid IL or missing references)
			((Graphic)hiImg).color = normalBg;
			try
			{
				textColorProp?.SetValue(textComp, normalTextColor);
			}
			catch
			{
			}
		});
		obj13.triggers.Add(val6);
		return val;
	}

	private static void TryOverrideNativeDialogBodyText(Transform root, string? newText)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(newText))
			{
				return;
			}
			// Primary path: TraderDialogWindow._traderText (TMPro.TextMeshProUGUI) is the NPC body text field
			object dialogWindow = GetDialogWindow(((Component)root).GetComponent<MonoBehaviour>() ?? (MonoBehaviour)(object)root.GetComponentInChildren<MonoBehaviour>(true));
			if (dialogWindow == null)
			{
				// Try finding _dialogWindow directly
				MonoBehaviour[] mbs = ((Component)root).GetComponentsInChildren<MonoBehaviour>(true);
				foreach (MonoBehaviour mb in mbs)
				{
					if (mb == null) continue;
					object candidate = ((object)mb).GetType()
						.GetField("_dialogWindow", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
						?.GetValue(mb);
					if (candidate != null) { dialogWindow = candidate; break; }
				}
			}
			if (dialogWindow != null)
			{
				object traderTextField = dialogWindow.GetType()
					.GetField("_traderText", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
					?.GetValue(dialogWindow);
				if (traderTextField != null)
				{
					PropertyInfo textProp = traderTextField.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
					if (textProp != null)
					{
						textProp.SetValue(traderTextField, newText, null);
						VisitPlugin.Log.LogInfo((object)("[NpcText] _traderText set via _dialogWindow"));
						return;
					}
				}
			}
			// Fallback: find the longest active text in the hierarchy (heuristic)
			Component val = null;
			int num = -1;
			Text[] componentsInChildren = ((Component)root).GetComponentsInChildren<Text>(true);
			foreach (Text val2 in componentsInChildren)
			{
				if (!((UnityEngine.Object)(object)val2 == (UnityEngine.Object)null) && ((Component)val2).gameObject.activeInHierarchy)
				{
					string text = val2.text;
					if (!string.IsNullOrWhiteSpace(text) && text.Length >= 5 && text.IndexOf("Show history", StringComparison.OrdinalIgnoreCase) < 0 && text.IndexOf("显示历史", StringComparison.OrdinalIgnoreCase) < 0 && text.Length > num)
					{
						num = text.Length;
						val = (Component)(object)val2;
					}
				}
			}
			Type type = FindType("TMPro.TMP_Text");
			if (type != null)
			{
				PropertyInfo property = type.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
				if (property != null)
				{
					MonoBehaviour[] componentsInChildren2 = ((Component)root).GetComponentsInChildren<MonoBehaviour>(true);
					foreach (MonoBehaviour val3 in componentsInChildren2)
					{
						if (!((UnityEngine.Object)(object)val3 == (UnityEngine.Object)null) && ((Component)val3).gameObject.activeInHierarchy && type.IsAssignableFrom(((object)val3).GetType()))
						{
							string text2 = null;
							try { text2 = property.GetValue(val3, null) as string; } catch { }
							if (!string.IsNullOrWhiteSpace(text2) && text2.Length >= 5 && text2.IndexOf("Show history", StringComparison.OrdinalIgnoreCase) < 0 && text2.IndexOf("显示历史", StringComparison.OrdinalIgnoreCase) < 0 && text2.Length > num)
							{
								num = text2.Length;
								val = (Component)(object)val3;
							}
						}
					}
				}
			}
			if ((UnityEngine.Object)(object)val == (UnityEngine.Object)null) return;
			Text val4 = (Text)(object)((val is Text) ? val : null);
			if (val4 != null) { val4.text = newText; return; }
			((object)val).GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public)?.SetValue(val, newText, null);
		}
		catch
		{
		}
	}

	private static Font? FindEftFont(Transform root)
	{
		try
		{
			Text[] componentsInChildren = ((Component)root).GetComponentsInChildren<Text>(true);
			foreach (Text val in componentsInChildren)
			{
				if (!((UnityEngine.Object)(object)val == (UnityEngine.Object)null) && !((UnityEngine.Object)(object)val.font == (UnityEngine.Object)null) && ((UnityEngine.Object)val.font).name.IndexOf("arial", StringComparison.OrdinalIgnoreCase) < 0)
				{
					return val.font;
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private static object? FindSceneTmpFont(Transform hint)
	{
		Type type = FindType("TMPro.TMP_Text");
		if (type == null)
		{
			return null;
		}
		PropertyInfo property = type.GetProperty("font", BindingFlags.Instance | BindingFlags.Public);
		if (property == null)
		{
			return null;
		}
		try
		{
			MonoBehaviour[] componentsInChildren = ((Component)hint).GetComponentsInChildren<MonoBehaviour>(true);
			foreach (MonoBehaviour val in componentsInChildren)
			{
				if (!((UnityEngine.Object)(object)val == (UnityEngine.Object)null) && type.IsAssignableFrom(((object)val).GetType()))
				{
					object value = property.GetValue(val);
					if (value != null)
					{
						return value;
					}
				}
			}
		}
		catch
		{
		}
		try
		{
			MonoBehaviour[] componentsInChildren = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>();
			foreach (MonoBehaviour val2 in componentsInChildren)
			{
				if (!((UnityEngine.Object)(object)val2 == (UnityEngine.Object)null) && type.IsAssignableFrom(((object)val2).GetType()))
				{
					object value2 = property.GetValue(val2);
					if (value2 != null)
					{
						return value2;
					}
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private static void WireRowClick(Transform row, MonoBehaviour screen, Action? callback = null)
	{
		//IL_009b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a5: Expected O, but got Unknown
		//IL_0247: Unknown result type (might be due to invalid IL or missing references)
		//IL_024c: Unknown result type (might be due to invalid IL or missing references)
		//IL_024e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0255: Expected O, but got Unknown
		//IL_0224: Unknown result type (might be due to invalid IL or missing references)
		//IL_022a: Invalid comparison between Unknown and I4
		MonoBehaviour screen2 = screen;
		try
		{
			if ((UnityEngine.Object)(object)row == (UnityEngine.Object)null)
			{
				return;
			}
			Action close = callback ?? ((Action)delegate
			{
				CloseNativeDialog(screen2);
			});
			ForceEnableNativeDialogInteraction(screen2);
			VisitApiInjectedOption visitApiInjectedOption = ((Component)row).GetComponent<VisitApiInjectedOption>() ?? ((Component)row).GetComponentInChildren<VisitApiInjectedOption>(true);
			if ((UnityEngine.Object)(object)visitApiInjectedOption != (UnityEngine.Object)null && visitApiInjectedOption.Callback == null)
			{
				visitApiInjectedOption.Callback = close;
			}
			Button componentInChildren = ((Component)row).GetComponentInChildren<Button>(true);
			if ((UnityEngine.Object)(object)componentInChildren != (UnityEngine.Object)null)
			{
				((UnityEventBase)componentInChildren.onClick).RemoveAllListeners();
				((UnityEvent)componentInChildren.onClick).AddListener((UnityAction)delegate
				{
					close();
				});
			}
			Toggle componentInChildren2 = ((Component)row).GetComponentInChildren<Toggle>(true);
			if ((UnityEngine.Object)(object)componentInChildren2 != (UnityEngine.Object)null)
			{
				((UnityEventBase)componentInChildren2.onValueChanged).RemoveAllListeners();
				((UnityEvent<bool>)(object)componentInChildren2.onValueChanged).AddListener((UnityAction<bool>)delegate(bool v)
				{
					if (v)
					{
						close();
					}
				});
			}
			Type type = FindType("EFT.UI.ClickTrigger") ?? FindType("ClickTrigger");
			if (type != null)
			{
				Component obj = ((Component)row).GetComponentInChildren(type, true) ?? ((Component)row).GetComponent(type) ?? ((Component)row).gameObject.AddComponent(type);
				MethodInfo method = type.GetMethod("Init", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (method != null)
				{
					ParameterInfo[] parameters = method.GetParameters();
					if (parameters.Length == 1)
					{
						Type parameterType = parameters[0].ParameterType;
						if (parameterType == typeof(Action<PointerEventData>))
						{
							Action<PointerEventData> action = delegate
							{
								close();
							};
							method.Invoke(obj, new object[1] { action });
						}
						else if (parameterType == typeof(Action<BaseEventData>))
						{
							Action<BaseEventData> action2 = delegate
							{
								close();
							};
							method.Invoke(obj, new object[1] { action2 });
						}
					}
				}
			}
			EventTrigger val = ((Component)row).GetComponent<EventTrigger>() ?? ((Component)row).gameObject.AddComponent<EventTrigger>();
			if (val.triggers == null)
			{
				val.triggers = new List<EventTrigger.Entry>();
			}
			for (int num = val.triggers.Count - 1; num >= 0; num--)
			{
				EventTrigger.Entry obj2 = val.triggers[num];
				if (obj2 != null && (int)obj2.eventID == 4)
				{
					val.triggers.RemoveAt(num);
				}
			}
			EventTrigger.Entry val2 = new EventTrigger.Entry
			{
				eventID = (EventTriggerType)4
			};
			((UnityEvent<BaseEventData>)(object)val2.callback).AddListener((UnityAction<BaseEventData>)delegate
			{
				close();
			});
			val.triggers.Add(val2);
		}
		catch (Exception ex)
		{
			VisitPlugin.Log.LogError((object)ex);
		}
	}

	// 等待两帧让原生 Show() 完成布局后再注入对话节点；期间先隐藏 DialogContainer 防止原生选项闪现
	private static IEnumerator InjectAfterDelay(MonoBehaviour screen, DialogTree tree, string nodeId)
	{
		Transform dialogCtPre = ((Component)screen).transform.Find("DialogContainer");
		if ((UnityEngine.Object)(object)dialogCtPre != (UnityEngine.Object)null)
		{
			((Component)dialogCtPre).gameObject.SetActive(false);
		}
		yield return null;
		yield return null;
		object? dialogWindow = GetDialogWindow(screen);
		Component dwComp = (Component)((dialogWindow is Component) ? dialogWindow : null);
		if (dwComp != null && (UnityEngine.Object)(object)dwComp != (UnityEngine.Object)null && !dwComp.gameObject.activeSelf)
		{
			dwComp.gameObject.SetActive(true);
		}
		if (!tree.Nodes.TryGetValue(nodeId, out DialogNode node))
		{
			if ((UnityEngine.Object)(object)dialogCtPre != (UnityEngine.Object)null)
			{
				((Component)dialogCtPre).gameObject.SetActive(true);
			}
			RunDialogNode(screen, tree, nodeId);
			yield break;
		}
		Queue<string> queue = new Queue<string>();
		if (node.Narration != null)
		{
			foreach (string line in node.Narration)
			{
				queue.Enqueue(SubstituteVars(line));
			}
		}
		List<string>? npcTextLines = node.NpcTextLines;
		if (npcTextLines != null && npcTextLines.Count > 1)
		{
			foreach (string line in npcTextLines)
			{
				queue.Enqueue(SubstituteVars(line));
			}
		}
		else if (!string.IsNullOrWhiteSpace(node.NpcText) && queue.Count == 0)
		{
			queue.Enqueue(SubstituteVars(node.NpcText));
		}
		if (queue.Count > 0)
		{
			RunNarrationPhase(screen, tree, nodeId, queue);
		}
		else
		{
			if ((UnityEngine.Object)(object)dialogCtPre != (UnityEngine.Object)null)
			{
				((Component)dialogCtPre).gameObject.SetActive(true);
			}
			RunDialogNode(screen, tree, nodeId);
		}
	}

	private static void TrySetSubtitleViewText(Component? sv, string text)
	{
		if ((UnityEngine.Object)(object)sv == (UnityEngine.Object)null)
		{
			return;
		}
		// EFT.UI.SubtitlesView has _textField: TMPro.TMP_Text directly on the component
		try
		{
			object textField = ((object)sv).GetType()
				.GetField("_textField", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				?.GetValue(sv);
			if (textField != null)
			{
				((object)textField).GetType()
					.GetProperty("text", BindingFlags.Instance | BindingFlags.Public)
					?.SetValue(textField, text);
				VisitPlugin.Log.LogInfo((object)("[SubtitlesView] _textField set: " + text));
				return;
			}
		}
		catch (Exception ex)
		{
			VisitPlugin.Log.LogWarning((object)("[SubtitlesView] _textField reflection failed: " + ex.Message));
		}
		// Fallback: scan children for any TMP or legacy Text component
		Type tmpType = FindType("TMPro.TMP_Text");
		for (int i = 0; i < sv.transform.childCount; i++)
		{
			Transform child = sv.transform.GetChild(i);
			if (tmpType != null)
			{
				Component tmpComp = ((Component)child).GetComponent(tmpType);
				if ((UnityEngine.Object)(object)tmpComp != (UnityEngine.Object)null)
				{
					try
					{
						tmpType.GetProperty("text", BindingFlags.Instance | BindingFlags.Public)?.SetValue(tmpComp, text);
						return;
					}
					catch { }
				}
			}
			Text legacyText = ((Component)child).GetComponent<Text>();
			if ((UnityEngine.Object)(object)legacyText != (UnityEngine.Object)null)
			{
				legacyText.text = text;
				return;
			}
		}
	}

	internal static void RunDialogNode(MonoBehaviour screen, DialogTree tree, string nodeId, bool skipNarration = false)
	{
		MonoBehaviour screen2 = screen;
		DialogTree tree2 = tree;
		string nodeId2 = nodeId;
		if (!tree2.Nodes.TryGetValue(nodeId2, out DialogNode value))
		{
			VisitPlugin.Log.LogWarning((object)("RunDialogNode: node '" + nodeId2 + "' not found; closing dialog"));
			DeselectCurrentVisitTab();
			CleanupDialogVisuals(screen2);
			CloseNativeDialog(screen2);
			return;
		}
		string text = DialogTreeLoader.ResolvePath(value.Background);
		if (text != null)
		{
			ChangeDialogBackground(screen2, text);
		}
		RemoveOverlayExit(((Component)screen2).transform);
		if (!skipNarration)
		{
			Queue<string> queue = new Queue<string>();
			if (value.Narration != null && value.Narration.Count > 0)
			{
				foreach (string item in value.Narration)
				{
					queue.Enqueue(SubstituteVars(item));
				}
			}
			List<string>? npcTextLines = value.NpcTextLines;
			if (npcTextLines != null && npcTextLines.Count > 1)
			{
				foreach (string npcTextLine in npcTextLines)
				{
					queue.Enqueue(SubstituteVars(npcTextLine));
				}
			}
			if (queue.Count > 0)
			{
				RunNarrationPhase(screen2, tree2, nodeId2, queue);
				return;
			}
		}
		object dialogWindow = GetDialogWindow(screen2);
		Type type = dialogWindow?.GetType();
		Transform val = ((type != null) ? GetLinesContainerTransform(dialogWindow, type) : null);
		MethodInfo methodInfo = ((type != null) ? FindAddLineMethod(type) : null);
		ClearInjectedOptions(val);
		if (!string.IsNullOrWhiteSpace(value.NpcText))
		{
			TryOverrideNativeDialogBodyText(((Component)screen2).transform, SubstituteVars(value.NpcText));
		}
		VisitPlugin.Log.LogInfo((object)("RunDialogNode '" + nodeId2 + "': dw=" + (type?.Name ?? "null") + " lines=" + (((val != null) ? ((UnityEngine.Object)val).name : null) ?? "null") + " addLine=" + (methodInfo?.Name ?? "null")));
		if ((UnityEngine.Object)(object)val != (UnityEngine.Object)null)
		{
			ActivateToRoot(val, ((Component)screen2).transform);
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (DialogOption option in value.Options)
		{
			if (!string.IsNullOrEmpty(option.QuestId) && (option.ShowWhenStatus != null || option.HideWhenStatus != null))
			{
				hashSet.Add(option.QuestId);
			}
		}
		if (hashSet.Count > 0 && !string.IsNullOrEmpty(_cachedProfileId))
		{
			QuestStatusCache.BatchFetch(_cachedProfileId, hashSet);
		}
		Type ctType = FindType("EFT.UI.ClickTrigger") ?? FindType("ClickTrigger");
		for (int i = 0; i < value.Options.Count; i++)
		{
			DialogOption dialogOption = value.Options[i];
			if ((dialogOption.Once && DialogStateStore.IsSeen(tree2.TraderId, _cachedProfileId, nodeId2, i)) || !QuestStatusCache.IsVisible(dialogOption))
			{
				continue;
			}
			string text2 = SubstituteVars(dialogOption.Text);
			string next = dialogOption.Next;
			Action action = ((!string.IsNullOrEmpty(dialogOption.Action)) ? BuildActionCallback(screen2, tree2, dialogOption.Action, next, dialogOption.QuestId, dialogOption.AcceptQuestId) : ((next == null) ? ((Action)delegate
			{
				DeselectCurrentVisitTab();
				CleanupDialogVisuals(screen2);
				CloseNativeDialog(screen2);
			}) : ((!(next == "@start")) ? ((Action)delegate
			{
				RunDialogNode(screen2, tree2, next);
			}) : ((Action)delegate
			{
				RunDialogNode(screen2, tree2, ResolveStartNode(tree2));
			}))));
			if (dialogOption.Once)
			{
				int capturedOi = i;
				Action capturedCb = action;
				action = delegate
				{
					DialogStateStore.MarkSeen(tree2.TraderId, _cachedProfileId, nodeId2, capturedOi);
					capturedCb();
				};
			}
			Transform val2 = null;
			if (dialogWindow != null && methodInfo != null)
			{
				val2 = TryNativeAddLine(dialogWindow, methodInfo, val, text2, action);
			}
			if ((UnityEngine.Object)(object)val2 != (UnityEngine.Object)null)
			{
				((Component)val2).gameObject.AddComponent<VisitApiInjectedOption>().Callback = action;
				ResetRowVisualState(val2);
				continue;
			}
			Component val3 = (Component)((dialogWindow is Component) ? dialogWindow : null);
			Transform root = ((val3 != null) ? val3.transform : ((Component)screen2).transform);
			Transform val4 = FindOptionsContainer(root) ?? GetOrCreateOverlayContainer(root);
			ActivateToRoot(val4, ((Component)screen2).transform);
			Transform val5 = FindOptionTemplate(((Component)screen2).transform, val4);
			GameObject val6;
			if ((UnityEngine.Object)(object)val5 != (UnityEngine.Object)null)
			{
				val6 = UnityEngine.Object.Instantiate<GameObject>(((Component)val5).gameObject, val4, false);
				((UnityEngine.Object)val6).name = "VisitAPI.Option";
				val6.SetActive(true);
				StripEftHandlers(val6.transform, ctType);
			}
			else
			{
				val6 = BuildOverlayRow(val4);
			}
			val6.AddComponent<VisitApiInjectedOption>();
			SetAnyLabel(val6, text2);
			WireRowClick(val6.transform, screen2, action);
		}
		ForceEnableNativeDialogInteraction(screen2);
	}

	private static void RunNarrationPhase(MonoBehaviour screen, DialogTree tree, string nodeId, Queue<string> queue)
	{
		MonoBehaviour screen2 = screen;
		DialogTree tree2 = tree;
		string nodeId2 = nodeId;
		Queue<string> queue2 = queue;
		Component sv = default(Component);
		ref Component reference = ref sv;
		object obj = ((object)screen2).GetType().GetField("_subtitlesView", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(screen2);
		reference = (Component)((obj is Component) ? obj : null);
		Transform dialogCt = ((Component)screen2).transform.Find("DialogContainer");
		if ((UnityEngine.Object)(object)sv != (UnityEngine.Object)null)
		{
			sv.gameObject.SetActive(true);
		}
		if ((UnityEngine.Object)(object)dialogCt != (UnityEngine.Object)null)
		{
			((Component)dialogCt).gameObject.SetActive(false);
		}
		string text = queue2.Dequeue();
		TrySetSubtitleViewText(sv, text);
		VisitUiController.SetNarrationClickHandler((queue2.Count == 0) ? ((Action)delegate
		{
			VisitUiController.SetNarrationClickHandler(null);
			if ((UnityEngine.Object)(object)sv != (UnityEngine.Object)null)
			{
				sv.gameObject.SetActive(false);
			}
			if ((UnityEngine.Object)(object)dialogCt != (UnityEngine.Object)null)
			{
				((Component)dialogCt).gameObject.SetActive(true);
			}
			RunDialogNode(screen2, tree2, nodeId2, skipNarration: true);
		}) : ((Action)delegate
		{
			RunNarrationPhase(screen2, tree2, nodeId2, queue2);
		}));
		ForceEnableNativeDialogInteraction(screen2);
	}

	private static string SubstituteVars(string text)
	{
		if (_cachedPlayerName.Length == 0)
		{
			return text;
		}
		return text.Replace("{player}", _cachedPlayerName).Replace("{playerName}", _cachedPlayerName);
	}

	private static void ClearInjectedOptions(Transform? linesContainer)
	{
		if ((UnityEngine.Object)(object)linesContainer != (UnityEngine.Object)null)
		{
			Transform[] array = (Transform[])(object)new Transform[linesContainer.childCount];
			for (int i = 0; i < array.Length; i++)
			{
				array[i] = linesContainer.GetChild(i);
			}
			foreach (Transform val in array)
			{
				if (!((UnityEngine.Object)(object)val == (UnityEngine.Object)null))
				{
					val.SetParent((Transform)null, false);
					UnityEngine.Object.Destroy((UnityEngine.Object)(object)((Component)val).gameObject);
				}
			}
		}
		VisitApiInjectedOption[] array3 = UnityEngine.Object.FindObjectsOfType<VisitApiInjectedOption>(true);
		foreach (VisitApiInjectedOption visitApiInjectedOption in array3)
		{
			if ((UnityEngine.Object)(object)visitApiInjectedOption != (UnityEngine.Object)null)
			{
				UnityEngine.Object.Destroy((UnityEngine.Object)(object)((Component)visitApiInjectedOption).gameObject);
			}
		}
	}

	private static void StripEftHandlers(Transform row, Type? ctType)
	{
		//IL_01c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01cc: Invalid comparison between Unknown and I4
		if (ctType != null)
		{
			MethodInfo method = ctType.GetMethod("Init", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			Component[] componentsInChildren = ((Component)row).GetComponentsInChildren(ctType, true);
			foreach (Component val in componentsInChildren)
			{
				if ((UnityEngine.Object)(object)val == (UnityEngine.Object)null)
				{
					continue;
				}
				if (method != null)
				{
					ParameterInfo[] parameters = method.GetParameters();
					if (parameters.Length == 1)
					{
						try
						{
							if (parameters[0].ParameterType == typeof(Action<PointerEventData>))
							{
								method.Invoke(val, new object[1] { (Action<PointerEventData>)delegate
								{
								} });
							}
							else if (parameters[0].ParameterType == typeof(Action<BaseEventData>))
							{
								method.Invoke(val, new object[1] { (Action<BaseEventData>)delegate
								{
								} });
							}
						}
						catch
						{
						}
					}
				}
				MonoBehaviour val2 = (MonoBehaviour)(object)((val is MonoBehaviour) ? val : null);
				if (val2 != null)
				{
					((Behaviour)val2).enabled = false;
				}
			}
		}
		Button[] componentsInChildren2 = ((Component)row).GetComponentsInChildren<Button>(true);
		foreach (Button val3 in componentsInChildren2)
		{
			if ((UnityEngine.Object)(object)val3 != (UnityEngine.Object)null)
			{
				((UnityEventBase)val3.onClick).RemoveAllListeners();
			}
		}
		Toggle[] componentsInChildren3 = ((Component)row).GetComponentsInChildren<Toggle>(true);
		foreach (Toggle val4 in componentsInChildren3)
		{
			if ((UnityEngine.Object)(object)val4 != (UnityEngine.Object)null)
			{
				((UnityEventBase)val4.onValueChanged).RemoveAllListeners();
			}
		}
		EventTrigger[] componentsInChildren4 = ((Component)row).GetComponentsInChildren<EventTrigger>(true);
		foreach (EventTrigger val5 in componentsInChildren4)
		{
			if (((val5 != null) ? val5.triggers : null) == null)
			{
				continue;
			}
			for (int num = val5.triggers.Count - 1; num >= 0; num--)
			{
				EventTrigger.Entry obj2 = val5.triggers[num];
				if (obj2 != null && (int)obj2.eventID == 4)
				{
					val5.triggers.RemoveAt(num);
				}
			}
		}
	}

	private static Transform? FindOptionTemplate(Transform screenRoot, Transform container)
	{
		Type ctType = FindType("EFT.UI.ClickTrigger") ?? FindType("ClickTrigger");
		for (int i = 0; i < container.childCount; i++)
		{
			Transform child = container.GetChild(i);
			if ((UnityEngine.Object)(object)child != (UnityEngine.Object)null && HasClickable(child))
			{
				return child;
			}
		}
		Transform val = ((Component)screenRoot).GetComponentsInChildren<Transform>(true).FirstOrDefault((Transform t) => (UnityEngine.Object)(object)t != (UnityEngine.Object)null && HasClickable(t) && ((UnityEngine.Object)t).name.IndexOf("option", StringComparison.OrdinalIgnoreCase) >= 0);
		if ((UnityEngine.Object)(object)val != (UnityEngine.Object)null)
		{
			return val;
		}
		if (ctType != null)
		{
			return ((Component)screenRoot).GetComponentsInChildren<Transform>(true).FirstOrDefault((Transform t) => (UnityEngine.Object)(object)t != (UnityEngine.Object)null && (UnityEngine.Object)(object)((Component)t).GetComponent<RectTransform>() != (UnityEngine.Object)null && (UnityEngine.Object)(object)((Component)t).GetComponentInChildren(ctType, true) != (UnityEngine.Object)null);
		}
		return null;
		bool HasClickable(Transform t)
		{
			if (!((UnityEngine.Object)(object)((Component)t).GetComponentInChildren<Button>(true) != (UnityEngine.Object)null) && !((UnityEngine.Object)(object)((Component)t).GetComponentInChildren<Toggle>(true) != (UnityEngine.Object)null))
			{
				if (ctType != null)
				{
					return (UnityEngine.Object)(object)((Component)t).GetComponentInChildren(ctType, true) != (UnityEngine.Object)null;
				}
				return false;
			}
			return true;
		}
	}

	private static void ForceEnableNativeDialogInteraction(MonoBehaviour screen)
	{
		try
		{
			CanvasGroup[] componentsInChildren = ((Component)screen).GetComponentsInChildren<CanvasGroup>(true);
			foreach (CanvasGroup val in componentsInChildren)
			{
				if (!((UnityEngine.Object)(object)val == (UnityEngine.Object)null))
				{
					val.alpha = 1f;
					val.interactable = true;
					val.blocksRaycasts = true;
					val.ignoreParentGroups = true;
				}
			}
			Selectable[] componentsInChildren2 = ((Component)screen).GetComponentsInChildren<Selectable>(true);
			foreach (Selectable val2 in componentsInChildren2)
			{
				if (!((UnityEngine.Object)(object)val2 == (UnityEngine.Object)null))
				{
					val2.interactable = true;
					((Behaviour)val2).enabled = true;
				}
			}
			Canvas val3 = ((Component)screen).GetComponent<Canvas>() ?? ((Component)screen).GetComponentInParent<Canvas>(true);
			if ((UnityEngine.Object)(object)val3 != (UnityEngine.Object)null)
			{
				GraphicRaycaster val4 = ((Component)val3).GetComponent<GraphicRaycaster>();
				if ((UnityEngine.Object)(object)val4 == (UnityEngine.Object)null)
				{
					val4 = ((Component)val3).gameObject.AddComponent<GraphicRaycaster>();
				}
				((Behaviour)val4).enabled = true;
			}
		}
		catch
		{
		}
	}

	private static List<Transform> FindOptionRowsByLabel(Transform root, string needle)
	{
		List<Transform> list = new List<Transform>();
		Text[] componentsInChildren = ((Component)root).GetComponentsInChildren<Text>(true);
		foreach (Text val in componentsInChildren)
		{
			if (!((UnityEngine.Object)(object)val == (UnityEngine.Object)null) && !string.IsNullOrWhiteSpace(val.text) && val.text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				list.Add(FindClickableRowRoot(((Component)val).transform));
			}
		}
		Type type = FindType("TMPro.TMP_Text");
		if (type == null)
		{
			return list;
		}
		PropertyInfo property = type.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
		if (property == null)
		{
			return list;
		}
		MonoBehaviour[] componentsInChildren2 = ((Component)root).GetComponentsInChildren<MonoBehaviour>(true);
		foreach (MonoBehaviour val2 in componentsInChildren2)
		{
			if (!((UnityEngine.Object)(object)val2 == (UnityEngine.Object)null) && type.IsAssignableFrom(((object)val2).GetType()))
			{
				string text = null;
				try
				{
					text = property.GetValue(val2, null) as string;
				}
				catch
				{
				}
				if (!string.IsNullOrWhiteSpace(text) && text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					list.Add(FindClickableRowRoot(((Component)val2).transform));
				}
			}
		}
		return list;
	}

	private static Transform FindClickableRowRoot(Transform leaf)
	{
		Transform val = leaf;
		for (int i = 0; i < 10; i++)
		{
			if (!((UnityEngine.Object)(object)val.parent != (UnityEngine.Object)null))
			{
				break;
			}
			if ((UnityEngine.Object)(object)((Component)val).GetComponent<Button>() != (UnityEngine.Object)null || (UnityEngine.Object)(object)((Component)val).GetComponent<Toggle>() != (UnityEngine.Object)null || (UnityEngine.Object)(object)((Component)val).GetComponent<EventTrigger>() != (UnityEngine.Object)null || HasClickTrigger((Component)(object)val) || (UnityEngine.Object)(object)((Component)val).GetComponent<LayoutElement>() != (UnityEngine.Object)null)
			{
				return val;
			}
			val = val.parent;
		}
		return leaf;
	}

	private static bool HasClickTrigger(Component c)
	{
		try
		{
			Type type = _cachedClickTriggerType;
			if (type == null)
			{
				type = (_cachedClickTriggerType = FindType("EFT.UI.ClickTrigger") ?? FindType("ClickTrigger"));
			}
			return type != null && (UnityEngine.Object)(object)c.GetComponent(type) != (UnityEngine.Object)null;
		}
		catch
		{
			return false;
		}
	}

	private static Transform? FindOptionsContainer(Transform root)
	{
		Transform result = null;
		int num = int.MinValue;
		RectTransform[] componentsInChildren = ((Component)root).GetComponentsInChildren<RectTransform>(true);
		foreach (RectTransform val in componentsInChildren)
		{
			if (!((UnityEngine.Object)(object)val == (UnityEngine.Object)null))
			{
				string obj = ((UnityEngine.Object)val).name ?? "";
				int num2 = 0;
				if (obj.IndexOf("line", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					num2 += 10;
				}
				if (obj.IndexOf("option", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					num2 += 10;
				}
				if (obj.IndexOf("container", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					num2 += 5;
				}
				if ((UnityEngine.Object)(object)((Component)val).GetComponent<VerticalLayoutGroup>() != (UnityEngine.Object)null)
				{
					num2 += 10;
				}
				if (((Transform)val).childCount >= 1)
				{
					num2 += 3;
				}
				if (num2 > num)
				{
					num = num2;
					result = (Transform)(object)val;
				}
			}
		}
		if (num < 13)
		{
			return null;
		}
		return result;
	}

	private static void SetAnyLabel(GameObject go, string label)
	{
		Text[] componentsInChildren = go.GetComponentsInChildren<Text>(true);
		for (int i = 0; i < componentsInChildren.Length; i++)
		{
			componentsInChildren[i].text = label;
		}
		Type type = FindType("TMPro.TMP_Text");
		if (type == null)
		{
			return;
		}
		PropertyInfo property = type.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
		if (property == null)
		{
			return;
		}
		MonoBehaviour[] componentsInChildren2 = go.GetComponentsInChildren<MonoBehaviour>(true);
		foreach (MonoBehaviour val in componentsInChildren2)
		{
			if (!((UnityEngine.Object)(object)val == (UnityEngine.Object)null) && type.IsAssignableFrom(((object)val).GetType()))
			{
				try
				{
					property.SetValue(val, label, null);
				}
				catch
				{
				}
			}
		}
	}

	private static void ActivateParentChain(Transform t)
	{
		List<GameObject> list = new List<GameObject>();
		Transform parent = t.parent;
		while ((UnityEngine.Object)(object)parent != (UnityEngine.Object)null)
		{
			if (!((Component)parent).gameObject.activeSelf)
			{
				list.Add(((Component)parent).gameObject);
			}
			parent = parent.parent;
		}
		for (int num = list.Count - 1; num >= 0; num--)
		{
			list[num].SetActive(true);
		}
	}

	private static string GetTransformPath(Transform t)
	{
		List<string> list = new List<string>();
		Transform val = t;
		while ((UnityEngine.Object)(object)val != (UnityEngine.Object)null)
		{
			list.Insert(0, ((UnityEngine.Object)val).name);
			val = val.parent;
		}
		return string.Join("/", list);
	}

	private static object? GetMemberValue(object obj, params string[] names)
	{
		Type type = obj.GetType();
		string[] array = names;
		foreach (string name in array)
		{
			try
			{
				PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (property != null)
				{
					object value = property.GetValue(obj);
					if (value != null)
					{
						return value;
					}
				}
			}
			catch
			{
			}
			try
			{
				FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (field != null)
				{
					object value2 = field.GetValue(obj);
					if (value2 != null)
					{
						return value2;
					}
				}
			}
			catch
			{
			}
		}
		array = names;
		foreach (string text in array)
		{
			PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			foreach (PropertyInfo propertyInfo in properties)
			{
				if (!(propertyInfo.PropertyType.Name == text))
				{
					continue;
				}
				try
				{
					object value3 = propertyInfo.GetValue(obj);
					if (value3 != null)
					{
						return value3;
					}
				}
				catch
				{
				}
			}
			FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			foreach (FieldInfo fieldInfo in fields)
			{
				if (!(fieldInfo.FieldType.Name == text))
				{
					continue;
				}
				try
				{
					object value4 = fieldInfo.GetValue(obj);
					if (value4 != null)
					{
						return value4;
					}
				}
				catch
				{
				}
			}
		}
		return null;
	}

	private void UpdateVisibility()
	{
		if ((UnityEngine.Object)(object)_tabGo == (UnityEngine.Object)null)
		{
			return;
		}
		bool flag = VisitPlugin.IsTraderRegistered(_lastTraderId) && IsVisitTabAllowed(_lastTraderId);
		_tabGo.SetActive(flag);
		if (flag)
		{
			SetAnyLabel(_tabGo, "拜访");
			if (!_dialogIsOpen)
			{
				DeselectVisitTab();
			}
		}
	}

	internal void DeselectVisitTab()
	{
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		_dialogIsOpen = false;
		VisitUiController.SetNarrationClickHandler(null);
		if (!((UnityEngine.Object)(object)_tabGo == (UnityEngine.Object)null))
		{
			Toggle componentInChildren = _tabGo.GetComponentInChildren<Toggle>(true);
			if ((UnityEngine.Object)(object)componentInChildren != (UnityEngine.Object)null)
			{
				componentInChildren.isOn = false;
			}
			SetTabTextColor(new Color(0.65f, 0.65f, 0.65f, 1f));
		}
	}

	private void SelectVisitTab()
	{
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		_dialogIsOpen = true;
		if (!((UnityEngine.Object)(object)_tabGo == (UnityEngine.Object)null))
		{
			Toggle componentInChildren = _tabGo.GetComponentInChildren<Toggle>(true);
			if ((UnityEngine.Object)(object)componentInChildren != (UnityEngine.Object)null)
			{
				componentInChildren.isOn = true;
			}
			SetTabTextColor(Color.white);
		}
	}

	private void SetTabTextColor(Color color)
	{
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b4: Unknown result type (might be due to invalid IL or missing references)
		if ((UnityEngine.Object)(object)_tabGo == (UnityEngine.Object)null)
		{
			return;
		}
		Text[] componentsInChildren = _tabGo.GetComponentsInChildren<Text>(true);
		foreach (Text val in componentsInChildren)
		{
			if ((UnityEngine.Object)(object)val != (UnityEngine.Object)null)
			{
				((Graphic)val).color = color;
			}
		}
		if ((object)_cachedTmpTextType == null)
		{
			_cachedTmpTextType = FindType("TMPro.TMP_Text");
		}
		Type cachedTmpTextType = _cachedTmpTextType;
		if (cachedTmpTextType == null)
		{
			return;
		}
		PropertyInfo property = cachedTmpTextType.GetProperty("color", BindingFlags.Instance | BindingFlags.Public);
		if (property == null)
		{
			return;
		}
		MonoBehaviour[] componentsInChildren2 = _tabGo.GetComponentsInChildren<MonoBehaviour>(true);
		foreach (MonoBehaviour val2 in componentsInChildren2)
		{
			if (!((UnityEngine.Object)(object)val2 == (UnityEngine.Object)null) && cachedTmpTextType.IsAssignableFrom(((object)val2).GetType()))
			{
				try
				{
					property.SetValue(val2, color);
				}
				catch
				{
				}
			}
		}
	}

	private static void DeselectCurrentVisitTab()
	{
		Component dealScreen = TraderDealScreenHook.DealScreen;
		if (!((UnityEngine.Object)(object)dealScreen == (UnityEngine.Object)null))
		{
			dealScreen.gameObject.GetComponent<TraderDealScreenVisitButton>()?.DeselectVisitTab();
		}
	}

	private void OnDestroy()
	{
		if ((UnityEngine.Object)(object)_tabGo != (UnityEngine.Object)null)
		{
			UnityEngine.Object.Destroy((UnityEngine.Object)(object)_tabGo);
			_tabGo = null;
		}
		_servicesAnchor = null;
	}

	private static object? GetDialogWindow(MonoBehaviour screen)
	{
		Type type = ((object)screen).GetType();
		while (type != null)
		{
			FieldInfo field = type.GetField("_dialogWindow", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field != null)
			{
				return field.GetValue(screen);
			}
			type = type.BaseType;
		}
		return null;
	}

	private static Transform? GetLinesContainerTransform(object dw, Type dwType)
	{
		string[] array = new string[5] { "_linesContainer", "linesContainer", "_lines", "lines_0", "container_0" };
		foreach (string name in array)
		{
			try
			{
				FieldInfo field = dwType.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (!(field == null))
				{
					object value = field.GetValue(dw);
					Component val = (Component)((value is Component) ? value : null);
					if (val != null && (UnityEngine.Object)(object)val != (UnityEngine.Object)null)
					{
						return val.transform;
					}
				}
			}
			catch
			{
			}
		}
		return null;
	}

	private static MethodInfo? FindAddLineMethod(Type dwType)
	{
		Type type = dwType;
		while (type != null)
		{
			MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			foreach (MethodInfo methodInfo in methods)
			{
				if (methodInfo.Name.IndexOf("AddLine", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return methodInfo;
				}
			}
			type = type.BaseType;
		}
		return null;
	}

	private static Transform? TryNativeAddLine(object dw, MethodInfo addLine, Transform? linesContainer, string text, Action? callback = null)
	{
		int num = ((linesContainer != null) ? linesContainer.childCount : 0);
		object obj = null;
		try
		{
			ParameterInfo[] parameters = addLine.GetParameters();
			object[] array = new object[parameters.Length];
			for (int i = 0; i < parameters.Length; i++)
			{
				Type parameterType = parameters[i].ParameterType;
				if (parameterType == typeof(string))
				{
					array[i] = text;
				}
				else if (callback != null && parameterType == typeof(Action))
				{
					array[i] = callback;
				}
				else if (callback != null && parameterType.IsGenericType && parameterType.GetGenericTypeDefinition() == typeof(Action<>))
				{
					try
					{
						array[i] = typeof(TraderDealScreenVisitButton).GetMethod("WrapCallback", BindingFlags.Static | BindingFlags.NonPublic).MakeGenericMethod(parameterType.GetGenericArguments()[0]).Invoke(null, new object[1] { callback });
					}
					catch
					{
					}
				}
				else if (parameterType.IsValueType)
				{
					array[i] = Activator.CreateInstance(parameterType);
				}
			}
			obj = addLine.Invoke(dw, array);
		}
		catch (Exception ex)
		{
			VisitPlugin.Log.LogWarning((object)("TryNativeAddLine: " + (ex.InnerException?.Message ?? ex.Message)));
			return null;
		}
		Transform val = null;
		Component val2 = (Component)((obj is Component) ? obj : null);
		if (val2 != null)
		{
			val = val2.transform;
		}
		Transform val3 = (Transform)((obj is Transform) ? obj : null);
		if (val3 != null)
		{
			val = val3;
		}
		if ((UnityEngine.Object)(object)val == (UnityEngine.Object)null && (UnityEngine.Object)(object)linesContainer != (UnityEngine.Object)null && linesContainer.childCount > num)
		{
			val = linesContainer.GetChild(linesContainer.childCount - 1);
		}
		if ((UnityEngine.Object)(object)val == (UnityEngine.Object)null)
		{
			VisitPlugin.Log.LogWarning((object)"TryNativeAddLine: could not locate new row");
			return null;
		}
		((Component)val).gameObject.SetActive(true);
		SetAnyLabel(((Component)val).gameObject, text);
		VisitPlugin.Log.LogInfo((object)("TryNativeAddLine OK: '" + ((UnityEngine.Object)val).name + "' text='" + text + "'"));
		return val;
	}

	private static void ActivateToRoot(Transform child, Transform root)
	{
		Transform val = child;
		while ((UnityEngine.Object)(object)val != (UnityEngine.Object)null && (UnityEngine.Object)(object)val != (UnityEngine.Object)(object)root.parent)
		{
			if (!((Component)val).gameObject.activeSelf)
			{
				((Component)val).gameObject.SetActive(true);
			}
			val = val.parent;
		}
	}

	private static void InjectBackgroundIntoDialogScreen(MonoBehaviour screen, string bgPath)
	{
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Expected O, but got Unknown
		try
		{
			Transform val = ((Component)screen).transform.Find("VisitAPI.Background");
			if ((UnityEngine.Object)(object)val != (UnityEngine.Object)null && bgPath == _currentBgPath)
			{
				return;
			}
			if ((UnityEngine.Object)(object)val != (UnityEngine.Object)null)
			{
				UnityEngine.Object.Destroy((UnityEngine.Object)(object)((Component)val).gameObject);
			}
			_currentBgPath = null;
			if (File.Exists(bgPath))
			{
				GameObject val2 = new GameObject("VisitAPI.Background");
				val2.transform.SetParent(((Component)screen).transform, false);
				val2.transform.SetSiblingIndex(0);
				CanvasGroup obj = val2.AddComponent<CanvasGroup>();
				obj.alpha = 1f;
				obj.interactable = false;
				obj.blocksRaycasts = false;
				obj.ignoreParentGroups = true;
				if (IsVideoPath(bgPath))
				{
					InjectVideoBackground(bgPath, val2);
				}
				else
				{
					InjectImageBackground(bgPath, val2);
				}
				_currentBgPath = bgPath;
				VisitPlugin.Log.LogInfo((object)("Background injected: " + bgPath));
			}
		}
		catch (Exception ex)
		{
			VisitPlugin.Log.LogWarning((object)("InjectBackgroundIntoDialogScreen: " + ex.Message));
		}
	}

	private static void InjectImageBackground(string imagePath, GameObject bgGo)
	{
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Expected O, but got Unknown
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_0095: Unknown result type (might be due to invalid IL or missing references)
		//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c9: Unknown result type (might be due to invalid IL or missing references)
		byte[] array = File.ReadAllBytes(imagePath);
		Texture2D val = new Texture2D(2, 2, (TextureFormat)4, false);
		if (!ImageConversion.LoadImage(val, array, false))
		{
			UnityEngine.Object.Destroy((UnityEngine.Object)(object)val);
			return;
		}
		((Texture)val).wrapMode = (TextureWrapMode)1;
		((Texture)val).filterMode = (FilterMode)1;
		Image obj = bgGo.AddComponent<Image>();
		obj.sprite = Sprite.Create(val, new Rect(0f, 0f, (float)((Texture)val).width, (float)((Texture)val).height), new Vector2(0.5f, 0.5f), 100f);
		((Graphic)obj).color = Color.white;
		((Graphic)obj).raycastTarget = false;
		RectTransform rectTransform = ((Graphic)obj).rectTransform;
		rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
		rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
		rectTransform.pivot = new Vector2(0.5f, 0.5f);
		rectTransform.anchoredPosition = Vector2.zero;
		AspectRatioFitter obj2 = bgGo.AddComponent<AspectRatioFitter>();
		obj2.aspectMode = (AspectRatioFitter.AspectMode)4;
		obj2.aspectRatio = (float)((Texture)val).width / (float)((Texture)val).height;
	}

	private static void InjectVideoBackground(string videoPath, GameObject bgGo)
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Expected O, but got Unknown
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_0078: Unknown result type (might be due to invalid IL or missing references)
		//IL_0082: Unknown result type (might be due to invalid IL or missing references)
		//IL_011a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0124: Expected O, but got Unknown
		RenderTexture val = new RenderTexture(Screen.width, Screen.height, 0, (RenderTextureFormat)0);
		val.Create();
		RawImage obj = bgGo.AddComponent<RawImage>();
		obj.texture = (Texture)(object)val;
		((Graphic)obj).color = Color.white;
		((Graphic)obj).raycastTarget = false;
		RectTransform rectTransform = ((Graphic)obj).rectTransform;
		rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
		rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
		rectTransform.pivot = new Vector2(0.5f, 0.5f);
		rectTransform.anchoredPosition = Vector2.zero;
		AspectRatioFitter arf = bgGo.AddComponent<AspectRatioFitter>();
		arf.aspectMode = (AspectRatioFitter.AspectMode)4;
		arf.aspectRatio = (float)Screen.width / (float)Screen.height;
		bgGo.AddComponent<VisitApiVideoBackground>().Rt = val;
		VideoPlayer obj2 = bgGo.AddComponent<VideoPlayer>();
		obj2.source = (VideoSource)1;
		obj2.url = "file:///" + videoPath.Replace('\\', '/');
		obj2.renderMode = (VideoRenderMode)2;
		obj2.targetTexture = val;
		obj2.isLooping = true;
		obj2.playOnAwake = false;
		obj2.audioOutputMode = (VideoAudioOutputMode)0;
		obj2.prepareCompleted += delegate(VideoPlayer src)
		{
			if (src.width != 0 && src.height != 0)
			{
				arf.aspectRatio = (float)src.width / (float)src.height;
			}
			src.Play();
		};
		obj2.Prepare();
	}

	private static bool IsVideoPath(string path)
	{
		if (!path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".mov", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".avi", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase))
		{
			return path.EndsWith(".ogv", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static void ChangeDialogBackground(MonoBehaviour screen, string bgPath)
	{
		try
		{
			InjectBackgroundIntoDialogScreen(screen, bgPath);
		}
		catch (Exception ex)
		{
			VisitPlugin.Log.LogWarning((object)("ChangeDialogBackground: " + ex.Message));
		}
	}

	private static void ResetRowVisualState(Transform row)
	{
		MonoBehaviour[] components = ((Component)row).GetComponents<MonoBehaviour>();
		foreach (MonoBehaviour val in components)
		{
			if ((UnityEngine.Object)(object)val == (UnityEngine.Object)null)
			{
				continue;
			}
			MethodInfo method = ((object)val).GetType().GetMethod("method_1", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (!(method == null) && method.GetParameters().Length == 1 && !(method.GetParameters()[0].ParameterType != typeof(bool)))
			{
				try
				{
					method.Invoke(val, new object[1] { false });
					break;
				}
				catch
				{
					break;
				}
			}
		}
	}

	private static void SwitchToTab(MonoBehaviour screen, string[] modes, string[] fields, bool setTasksFlag = false)
	{
		_duringTabSwitch = true;
		try
		{
			DeselectCurrentVisitTab();
			CleanupDialogVisuals(screen);
			CloseNativeDialog(screen);
			if (setTasksFlag)
			{
				PluginActivatedTasksTab = true;
			}
			TryActivateTraderTab(modes, fields);
		}
		finally
		{
			_duringTabSwitch = false;
		}
	}

	private static Action BuildActionCallback(MonoBehaviour screen, DialogTree tree, string action, string? nextNode = null, string? questId = null, string? acceptQuestId = null)
	{
		MonoBehaviour screen2 = screen;
		DialogTree tree2 = tree;
		string nextNode2 = nextNode;
		if (action == "openTrade")
		{
			return delegate
			{
				SwitchToTab(screen2, new string[5] { "Deal", "Trade", "Trading", "交易", "Торговля" }, new string[3] { "_dealTab", "_tradeTab", "_tradingTab" });
			};
		}
		if (action == "openTasks")
		{
			return delegate
			{
				SwitchToTab(screen2, new string[6] { "Tasks", "Task", "Quest", "Quests", "任务", "Задания" }, new string[3] { "_tasksTab", "_questsTab", "_questTab" }, setTasksFlag: true);
			};
		}
		if (action == "acceptQuest" && !string.IsNullOrEmpty(questId))
		{
			string qid3 = questId;
			string pid2 = _cachedProfileId;
			string? aqid = acceptQuestId;
			return delegate
			{
				if (NativeQuestController.AcceptQuest(pid2, qid3))
				{
					QuestStatusCache.Set(qid3, 2);
				}
				if (!string.IsNullOrEmpty(aqid) && NativeQuestController.AcceptQuest(pid2, aqid))
				{
					QuestStatusCache.Set(aqid, 2);
				}
				NavigateOrClose(screen2, tree2, nextNode2);
			};
		}
		if (action == "handoverItems" && !string.IsNullOrEmpty(questId))
		{
			string qid2 = questId;
			string pid3 = _cachedProfileId;
			string? aqid2 = acceptQuestId;
			string nxt = nextNode2;
			return delegate
			{
				NativeQuestController.ShowNativeHandoverScreen(qid2, delegate(bool success)
				{
					if (success)
					{
						QuestStatusCache.Set(qid2, 3);
						if (!string.IsNullOrEmpty(aqid2) && NativeQuestController.AcceptQuest(pid3, aqid2))
						{
							QuestStatusCache.Set(aqid2, 2);
						}
					}
					NavigateOrClose(screen2, tree2, nxt);
				});
			};
		}
		if (action == "completeQuest" && !string.IsNullOrEmpty(questId))
		{
			string qid = questId;
			string pid = _cachedProfileId;
			string? aqid3 = acceptQuestId;
			return delegate
			{
				if (NativeQuestController.CompleteQuest(pid, qid))
				{
					QuestStatusCache.Set(qid, 4);
					if (!string.IsNullOrEmpty(aqid3) && NativeQuestController.AcceptQuest(pid, aqid3))
					{
						QuestStatusCache.Set(aqid3, 2);
					}
				}
				NavigateOrClose(screen2, tree2, nextNode2);
			};
		}
		return delegate
		{
			VisitPlugin.Log.LogWarning((object)("Unknown dialog action: " + action));
			CloseNativeDialog(screen2);
		};
	}

	private static void NavigateOrClose(MonoBehaviour screen, DialogTree tree, string? nextNode)
	{
		if (!string.IsNullOrEmpty(nextNode))
		{
			RunDialogNode(screen, tree, nextNode);
			return;
		}
		DeselectCurrentVisitTab();
		CleanupDialogVisuals(screen);
		CloseNativeDialog(screen);
	}

	private static void TryExtractTraderLoyalty(object? profile, string traderId)
	{
		if (profile == null)
		{
			return;
		}
		try
		{
			Type type = profile.GetType();
			object obj = null;
			Type type2 = type;
			while (type2 != null && obj == null)
			{
				try
				{
					obj = type2.GetProperty("TradersInfo", BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public)?.GetValue(profile);
				}
				catch
				{
				}
				type2 = type2.BaseType;
			}
			if (obj == null)
			{
				return;
			}
			object obj3 = null;
			Type type3 = FindType("MongoID") ?? FindType("EFT.MongoID");
			if (type3 != null)
			{
				try
				{
					obj3 = Activator.CreateInstance(type3, traderId);
				}
				catch
				{
				}
			}
			object obj5 = null;
			MethodInfo method = obj.GetType().GetMethod("get_Item", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (method != null)
			{
				try
				{
					obj5 = method.Invoke(obj, new object[1] { obj3 ?? traderId });
				}
				catch
				{
				}
			}
			if (obj5 != null)
			{
				Type type4 = obj5.GetType();
				object obj7 = type4.GetProperty("LoyaltyLevel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj5) ?? type4.GetField("LoyaltyLevel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj5);
				object obj8 = type4.GetProperty("Standing", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj5) ?? type4.GetField("Standing", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj5);
				if (obj7 != null)
				{
					_cachedTraderLevel = Convert.ToInt32(obj7);
				}
				if (obj8 != null)
				{
					_cachedTraderStanding = Convert.ToDouble(obj8);
				}
				VisitPlugin.Log.LogInfo((object)$"TraderLoyalty: traderId={traderId} level={_cachedTraderLevel} standing={_cachedTraderStanding:F3}");
			}
		}
		catch (Exception ex)
		{
			VisitPlugin.Log.LogWarning((object)("TryExtractTraderLoyalty: " + ex.Message));
		}
	}

	private static string ResolveStartNode(DialogTree tree)
	{
		if (tree.NodeConditions == null || tree.NodeConditions.Count == 0)
		{
			return tree.StartNode;
		}
		foreach (NodeCondition nodeCondition in tree.NodeConditions)
		{
			if (_cachedTraderLevel >= nodeCondition.MinLevel && _cachedTraderLevel <= nodeCondition.MaxLevel && !(_cachedTraderStanding < nodeCondition.MinStanding) && !(_cachedTraderStanding > nodeCondition.MaxStanding) && !string.IsNullOrEmpty(nodeCondition.Node))
			{
				VisitPlugin.Log.LogInfo((object)$"ResolveStartNode: condition matched → '{nodeCondition.Node}' (level={_cachedTraderLevel} standing={_cachedTraderStanding:F3})");
				return nodeCondition.Node;
			}
		}
		return tree.StartNode;
	}

	private static void CleanupDialogVisuals(MonoBehaviour screen)
	{
		ActiveNativeClose = null;
		VisitUiController.SetNarrationClickHandler(null);
		TraderDialogScreenPatch.DialogSuppressed = true;
		Transform val = ((Component)screen).transform.Find("VisitAPI.Background");
		if ((UnityEngine.Object)(object)val != (UnityEngine.Object)null)
		{
			UnityEngine.Object.Destroy((UnityEngine.Object)(object)((Component)val).gameObject);
		}
		_currentBgPath = null;
		VisitApiEscHandler component = ((Component)screen).GetComponent<VisitApiEscHandler>();
		if ((UnityEngine.Object)(object)component != (UnityEngine.Object)null)
		{
			component.CloseAction = null;
			UnityEngine.Object.Destroy((UnityEngine.Object)(object)component);
		}
		Canvas component2 = ((Component)screen).GetComponent<Canvas>();
		if ((UnityEngine.Object)(object)component2 != (UnityEngine.Object)null)
		{
			component2.overrideSorting = false;
		}
		SetIgnoreInputInNPCDialogReflection(ignore: false);
	}

	private static void TryActivateTraderTab(string[] modeCandidates, string[] fieldNames)
	{
		Component dealScreen = TraderDealScreenHook.DealScreen;
		if ((UnityEngine.Object)(object)dealScreen != (UnityEngine.Object)null)
		{
			MethodInfo method = ((object)dealScreen).GetType().GetMethod("OnTradingModeChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (method != null)
			{
				ParameterInfo[] parameters = method.GetParameters();
				if (parameters.Length == 1 && parameters[0].ParameterType.IsEnum)
				{
					Type parameterType = parameters[0].ParameterType;
					VisitPlugin.Log.LogInfo((object)("TryActivateTraderTab: ETradingMode=[" + string.Join(",", Enum.GetNames(parameterType)) + "]"));
					string[] array = modeCandidates;
					foreach (string text in array)
					{
						try
						{
							object obj = Enum.Parse(parameterType, text, ignoreCase: true);
							method.Invoke(dealScreen, new object[1] { obj });
							VisitPlugin.Log.LogInfo((object)("TryActivateTraderTab: OnTradingModeChanged(" + text + ") OK"));
							return;
						}
						catch
						{
						}
					}
					VisitPlugin.Log.LogWarning((object)("TryActivateTraderTab: no enum match for [" + string.Join(",", modeCandidates) + "]"));
				}
			}
		}
		Component screensGroup = TraderDealScreenHook.ScreensGroup;
		if ((UnityEngine.Object)(object)screensGroup != (UnityEngine.Object)null)
		{
			Component val = null;
			Transform val2 = null;
			string text2 = null;
			string[] array = fieldNames;
			foreach (string text3 in array)
			{
				FieldInfo field = ((object)screensGroup).GetType().GetField(text3, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (!(field == null))
				{
					object value = field.GetValue(screensGroup);
					Component val3 = (Component)((value is Component) ? value : null);
					object obj3 = ((val3 != null) ? val3.transform : null);
					if (obj3 == null)
					{
						GameObject val4 = (GameObject)((value is GameObject) ? value : null);
						obj3 = ((val4 != null) ? val4.transform : null);
					}
					Transform val5 = (Transform)obj3;
					if (!((UnityEngine.Object)(object)val5 == (UnityEngine.Object)null))
					{
						val = val3;
						val2 = val5;
						text2 = text3;
						break;
					}
				}
			}
			if ((UnityEngine.Object)(object)val2 != (UnityEngine.Object)null)
			{
				VisitPlugin.Log.LogInfo((object)("TryActivateTraderTab: field '" + text2 + "' → " + ((UnityEngine.Object)val2).name + " (" + (((object)val)?.GetType().FullName ?? "?") + ")"));
				array = new string[7] { "_tradingTab", "_tasksTab", "_servicesTab", "_dealTab", "_tradeTab", "_questsTab", "_questTab" };
				foreach (string text4 in array)
				{
					if (text4 == text2)
					{
						continue;
					}
					FieldInfo field2 = ((object)screensGroup).GetType().GetField(text4, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					if (field2 == null)
					{
						continue;
					}
					object value2 = field2.GetValue(screensGroup);
					Component val6 = (Component)((value2 is Component) ? value2 : null);
					if ((UnityEngine.Object)(object)val6 == (UnityEngine.Object)null)
					{
						continue;
					}
					MonoBehaviour[] components = ((Component)val6.transform).GetComponents<MonoBehaviour>();
					foreach (MonoBehaviour val7 in components)
					{
						if ((UnityEngine.Object)(object)val7 == (UnityEngine.Object)null)
						{
							continue;
						}
						Type type = ((object)val7).GetType();
						string[] array2 = new string[4] { "Deselect", "Unselect", "Hide", "Deactivate" };
						foreach (string text5 in array2)
						{
							MethodInfo method2 = type.GetMethod(text5, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
							if (!(method2 == null) && !method2.IsAbstract)
							{
								try
								{
									method2.Invoke(val7, null);
									VisitPlugin.Log.LogInfo((object)("TryActivateTraderTab: " + type.Name + "." + text5 + "() on '" + ((UnityEngine.Object)val6.gameObject).name + "'"));
								}
								catch (Exception ex)
								{
									VisitPlugin.Log.LogWarning((object)("TryActivateTraderTab: " + type.Name + "." + text5 + ": " + (ex.InnerException?.Message ?? ex.Message)));
									continue;
								}
								break;
							}
						}
					}
				}
				ActivateTabTransform(val2, val);
				return;
			}
		}
		Component servicesAnchor = TraderDealScreenHook.ServicesAnchor;
		if ((UnityEngine.Object)(object)servicesAnchor == (UnityEngine.Object)null)
		{
			VisitPlugin.Log.LogWarning((object)"TryActivateTraderTab: no anchor");
			return;
		}
		Transform parent = servicesAnchor.transform.parent;
		if ((UnityEngine.Object)(object)parent == (UnityEngine.Object)null)
		{
			VisitPlugin.Log.LogWarning((object)"TryActivateTraderTab: anchor has no parent");
			return;
		}
		for (int l = 0; l < parent.childCount; l++)
		{
			Transform child = parent.GetChild(l);
			VisitPlugin.Log.LogInfo((object)string.Format("TryActivateTraderTab: sibling[{0}]={1} isAnchor={2}", l, ((child != null) ? ((UnityEngine.Object)child).name : null) ?? "null", (UnityEngine.Object)(object)child == (UnityEngine.Object)(object)servicesAnchor.transform));
		}
		for (int m = 0; m < parent.childCount; m++)
		{
			Transform child2 = parent.GetChild(m);
			if (!((UnityEngine.Object)(object)child2 == (UnityEngine.Object)null) && !((UnityEngine.Object)(object)child2 == (UnityEngine.Object)(object)servicesAnchor.transform) && ChildHasAnyLabel(child2, modeCandidates))
			{
				VisitPlugin.Log.LogInfo((object)$"TryActivateTraderTab: label match at [{m}] = {((UnityEngine.Object)child2).name}");
				ActivateTabTransform(child2);
				return;
			}
		}
		VisitPlugin.Log.LogWarning((object)("TryActivateTraderTab: not found. candidates=[" + string.Join(",", modeCandidates) + "]"));
	}

	private static void ActivateTabTransform(Transform tab, Component? tabComp = null)
	{
		//IL_0331: Unknown result type (might be due to invalid IL or missing references)
		//IL_0340: Expected O, but got Unknown
		Toggle val = ((Component)tab).GetComponent<Toggle>() ?? ((Component)tab).GetComponentInChildren<Toggle>(true);
		if ((UnityEngine.Object)(object)val == (UnityEngine.Object)null)
		{
			Transform parent = tab.parent;
			int num = 0;
			while (num < 3 && (UnityEngine.Object)(object)parent != (UnityEngine.Object)null && (UnityEngine.Object)(object)val == (UnityEngine.Object)null)
			{
				Toggle component = ((Component)parent).GetComponent<Toggle>();
				if ((UnityEngine.Object)(object)component != (UnityEngine.Object)null && (UnityEngine.Object)(object)component.group != (UnityEngine.Object)null)
				{
					val = component;
				}
				num++;
				parent = parent.parent;
			}
		}
		if ((UnityEngine.Object)(object)val != (UnityEngine.Object)null)
		{
			val.isOn = true;
			VisitPlugin.Log.LogInfo((object)("ActivateTabTransform: Toggle on " + ((UnityEngine.Object)tab).name));
			return;
		}
		Button val2 = ((Component)tab).GetComponent<Button>() ?? ((Component)tab).GetComponentInChildren<Button>(true);
		if ((UnityEngine.Object)(object)val2 != (UnityEngine.Object)null)
		{
			((UnityEvent)val2.onClick).Invoke();
			VisitPlugin.Log.LogInfo((object)("ActivateTabTransform: Button on " + ((UnityEngine.Object)tab).name));
			return;
		}
		MonoBehaviour[] components = ((Component)tab).GetComponents<MonoBehaviour>();
		foreach (MonoBehaviour val3 in components)
		{
			if ((UnityEngine.Object)(object)val3 == (UnityEngine.Object)null)
			{
				continue;
			}
			Type type = ((object)val3).GetType();
			VisitPlugin.Log.LogInfo((object)("ActivateTabTransform: " + ((UnityEngine.Object)tab).name + " has " + type.FullName));
			string[] array = new string[4] { "Show", "Select", "Activate", "OnClick" };
			foreach (string text in array)
			{
				MethodInfo methodInfo = null;
				MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				foreach (MethodInfo methodInfo2 in methods)
				{
					if (methodInfo2.Name != text || methodInfo2.IsAbstract)
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
					ParameterInfo[] array2 = parameters;
					for (int l = 0; l < array2.Length; l++)
					{
						if (!array2[l].HasDefaultValue)
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
					continue;
				}
				try
				{
					ParameterInfo[] parameters2 = methodInfo.GetParameters();
					object[] array3 = null;
					if (parameters2.Length != 0)
					{
						array3 = new object[parameters2.Length];
						for (int m = 0; m < parameters2.Length; m++)
						{
							array3[m] = parameters2[m].DefaultValue;
						}
					}
					methodInfo.Invoke(val3, array3);
					VisitPlugin.Log.LogInfo((object)("ActivateTabTransform: " + type.Name + "." + text + "() OK on " + ((UnityEngine.Object)tab).name));
					if (text == "Select")
					{
						TraderDealScreenHook.SetPluginActivatedTab((Component?)(object)val3);
					}
					return;
				}
				catch (Exception ex)
				{
					VisitPlugin.Log.LogWarning((object)("ActivateTabTransform: " + type.Name + "." + text + ": " + (ex.InnerException?.Message ?? ex.Message)));
				}
			}
		}
		try
		{
			ExecuteEvents.Execute<IPointerClickHandler>(((Component)tab).gameObject, (BaseEventData)new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
			VisitPlugin.Log.LogInfo((object)("ActivateTabTransform: ExecuteEvents.pointerClick on " + ((UnityEngine.Object)tab).name));
			return;
		}
		catch (Exception ex2)
		{
			VisitPlugin.Log.LogWarning((object)("ActivateTabTransform: ExecuteEvents: " + ex2.Message));
		}
		VisitPlugin.Log.LogWarning((object)("ActivateTabTransform: " + ((UnityEngine.Object)tab).name + " - all activation attempts failed"));
	}

	private static bool ChildHasAnyLabel(Transform t, string[] needles)
	{
		Text[] componentsInChildren = ((Component)t).GetComponentsInChildren<Text>(true);
		foreach (Text val in componentsInChildren)
		{
			if ((UnityEngine.Object)(object)val == (UnityEngine.Object)null)
			{
				continue;
			}
			string[] array = needles;
			foreach (string value in array)
			{
				if (val.text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return true;
				}
			}
		}
		Type type = FindType("TMPro.TMP_Text");
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
				if (!(property.GetValue(val2, null) is string text))
				{
					continue;
				}
				string[] array = needles;
				foreach (string value2 in array)
				{
					if (text.IndexOf(value2, StringComparison.OrdinalIgnoreCase) >= 0)
					{
						return true;
					}
				}
			}
			catch
			{
			}
		}
		return false;
	}

	private static Action<T> WrapCallback<T>(Action action)
	{
		Action action2 = action;
		return delegate
		{
			action2();
		};
	}

	private static void SetIgnoreInputInNPCDialogReflection(bool ignore)
	{
		try
		{
			if (_s_setIgnoreInputInNpcDialog == null)
			{
				_s_setIgnoreInputInNpcDialog = FindType("EFT.GamePlayerOwner")?.GetMethod("SetIgnoreInputInNPCDialog", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			}
			_s_setIgnoreInputInNpcDialog?.Invoke(null, new object[1] { ignore });
		}
		catch
		{
		}
	}

	internal static readonly Dictionary<string, Type?> _typeCache = new Dictionary<string, Type?>(StringComparer.Ordinal);

	internal static Type? FindType(string fullName)
	{
		if (_typeCache.TryGetValue(fullName, out Type? cached))
		{
			return cached;
		}
		Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
		foreach (Assembly assembly in assemblies)
		{
			try
			{
				Type type = assembly.GetType(fullName, throwOnError: false);
				if (type != null)
				{
					_typeCache[fullName] = type;
					return type;
				}
			}
			catch
			{
			}
		}
		_typeCache[fullName] = null;
		return null;
	}

	private static void FixLayoutWidth(GameObject tabGo, Component servicesAnchor)
	{
		//IL_006a: Unknown result type (might be due to invalid IL or missing references)
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_007f: Unknown result type (might be due to invalid IL or missing references)
		//IL_008b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0097: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f5: Unknown result type (might be due to invalid IL or missing references)
		//IL_0103: Unknown result type (might be due to invalid IL or missing references)
		//IL_0108: Unknown result type (might be due to invalid IL or missing references)
		RectTransform component = servicesAnchor.GetComponent<RectTransform>();
		RectTransform component2 = tabGo.GetComponent<RectTransform>();
		if (!((UnityEngine.Object)(object)component == (UnityEngine.Object)null) && !((UnityEngine.Object)(object)component2 == (UnityEngine.Object)null))
		{
			LayoutElement component3 = servicesAnchor.GetComponent<LayoutElement>();
			LayoutElement val = tabGo.GetComponent<LayoutElement>() ?? tabGo.AddComponent<LayoutElement>();
			Rect rect;
			if ((UnityEngine.Object)(object)component3 != (UnityEngine.Object)null)
			{
				val.preferredWidth = component3.preferredWidth;
				val.minWidth = component3.minWidth;
				val.flexibleWidth = component3.flexibleWidth;
			}
			else
			{
				rect = component.rect;
				val.preferredWidth = rect.width;
			}
			component2.anchorMin = component.anchorMin;
			component2.anchorMax = component.anchorMax;
			component2.pivot = component.pivot;
			component2.sizeDelta = component.sizeDelta;
			Transform parent = servicesAnchor.transform.parent;
			if (!((UnityEngine.Object)(object)parent != (UnityEngine.Object)null) || (!((UnityEngine.Object)(object)((Component)parent).GetComponent<HorizontalOrVerticalLayoutGroup>() != (UnityEngine.Object)null) && !((UnityEngine.Object)(object)((Component)parent).GetComponent<GridLayoutGroup>() != (UnityEngine.Object)null)))
			{
				Vector2 anchoredPosition = component.anchoredPosition;
				rect = component.rect;
				component2.anchoredPosition = anchoredPosition + new Vector2(rect.width, 0f);
			}
		}
	}
}
