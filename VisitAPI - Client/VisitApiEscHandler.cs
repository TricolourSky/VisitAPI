using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VisitAPI;

[DefaultExecutionOrder(32000)]
internal sealed class VisitApiEscHandler : MonoBehaviour
{
	public Action? CloseAction;

	internal static bool IsActive;

	private static MethodInfo? _s_showCursorMethod;

	private static readonly object[] _showCursorArgs = new object[1] { true };

	private static VisitApiInjectedOption? _hoveredOption;

	private static void EftShowCursor()
	{
		try
		{
			if ((object)_s_showCursorMethod == null)
			{
				_s_showCursorMethod = AccessTools.TypeByName("GClass2304")?.GetMethod("smethod_0", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			}
			if (_s_showCursorMethod != null)
			{
				_s_showCursorMethod.Invoke(null, _showCursorArgs);
				return;
			}
		}
		catch
		{
		}
		Cursor.lockState = (CursorLockMode)0;
		Cursor.visible = true;
	}

	private void OnEnable()
	{
		IsActive = true;
		EftShowCursor();
	}

	private void Update()
	{
		EftShowCursor();
		if (Input.GetKeyDown((KeyCode)27) && CloseAction != null)
		{
			Action? closeAction = CloseAction;
			CloseAction = null;
			closeAction();
		}
	}

	private void LateUpdate()
	{
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		EftShowCursor();
		Vector3 mousePosition = Input.mousePosition;
		UpdateHover((Vector2)mousePosition);
		if (Input.GetMouseButtonDown(0))
		{
			TryHandleManualClick((Vector2)mousePosition);
		}
	}

	private static bool HitTest(VisitApiInjectedOption opt, Vector2 mousePos)
	{
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		RectTransform component = ((Component)opt).GetComponent<RectTransform>();
		if ((UnityEngine.Object)(object)component == (UnityEngine.Object)null)
		{
			return false;
		}
		Canvas componentInParent = ((Component)opt).GetComponentInParent<Canvas>();
		Camera val = (((UnityEngine.Object)(object)componentInParent != (UnityEngine.Object)null) ? componentInParent.rootCanvas.worldCamera : null);
		return RectTransformUtility.RectangleContainsScreenPoint(component, mousePos, val);
	}

	private static void UpdateHover(Vector2 mousePos)
	{
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		VisitApiInjectedOption visitApiInjectedOption = null;
		foreach (VisitApiInjectedOption item in VisitApiInjectedOption.Active)
		{
			if (!((UnityEngine.Object)(object)item == (UnityEngine.Object)null) && ((Behaviour)item).isActiveAndEnabled && HitTest(item, mousePos))
			{
				visitApiInjectedOption = item;
				break;
			}
		}
		if (!((UnityEngine.Object)(object)visitApiInjectedOption == (UnityEngine.Object)(object)_hoveredOption))
		{
			if ((UnityEngine.Object)(object)_hoveredOption != (UnityEngine.Object)null)
			{
				_hoveredOption.SetHover(value: false);
			}
			if ((UnityEngine.Object)(object)visitApiInjectedOption != (UnityEngine.Object)null)
			{
				visitApiInjectedOption.SetHover(value: true);
			}
			_hoveredOption = visitApiInjectedOption;
		}
	}

	private static void TryHandleManualClick(Vector2 mousePos)
	{
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		foreach (VisitApiInjectedOption item in VisitApiInjectedOption.Active)
		{
			if (!((UnityEngine.Object)(object)item == (UnityEngine.Object)null) && ((Behaviour)item).isActiveAndEnabled && item.Callback != null && HitTest(item, mousePos))
			{
				try
				{
					item.Callback();
					return;
				}
				catch (Exception arg)
				{
					VisitPlugin.Log.LogError((object)$"[ManualClick] {arg}");
					return;
				}
			}
		}
		Action pendingNarrationClick = VisitUiController.PendingNarrationClick;
		if (pendingNarrationClick != null)
		{
			try
			{
				pendingNarrationClick();
			}
			catch (Exception arg2)
			{
				VisitPlugin.Log.LogError((object)$"[ManualClick/Narration] {arg2}");
			}
		}
	}

	private void OnDisable()
	{
		IsActive = false;
		CloseAction = null;
		if ((UnityEngine.Object)(object)_hoveredOption != (UnityEngine.Object)null)
		{
			_hoveredOption.SetHover(value: false);
			_hoveredOption = null;
		}
	}
}
