using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VisitAPI;

// ── Narration overlay + click pass-through ─────────────────────────────────────

internal static class VisitUiController
{
	private static GameObject? _narrationOverlay;

	internal static Action? PendingNarrationClick;

	public static void SetNarrationClickHandler(Action? callback)
	{
		Action? callback2 = callback;
		PendingNarrationClick = callback2;
		if ((UnityEngine.Object)(object)_narrationOverlay != (UnityEngine.Object)null)
		{
			UnityEngine.Object.Destroy((UnityEngine.Object)(object)_narrationOverlay);
			_narrationOverlay = null;
		}
		if (callback2 == null) return;

		_narrationOverlay = new GameObject("VisitAPI.NarrationOverlay");
		UnityEngine.Object.DontDestroyOnLoad((UnityEngine.Object)(object)_narrationOverlay);
		Canvas canvas = _narrationOverlay.AddComponent<Canvas>();
		canvas.renderMode = (RenderMode)0;
		canvas.sortingOrder = 3500;
		_narrationOverlay.AddComponent<GraphicRaycaster>();
		GameObject clickGo = new GameObject("Click");
		clickGo.transform.SetParent(_narrationOverlay.transform, false);
		Image img = clickGo.AddComponent<Image>();
		((Graphic)img).color = new Color(0f, 0f, 0f, 0f);
		RectTransform rt = ((Graphic)img).rectTransform;
		rt.anchorMin = Vector2.zero;
		rt.anchorMax = Vector2.one;
		rt.offsetMin = Vector2.zero;
		rt.offsetMax = Vector2.zero;
		EventTrigger trigger = clickGo.AddComponent<EventTrigger>();
		EventTrigger.Entry entry = new EventTrigger.Entry { eventID = (EventTriggerType)4 };
		float activateAfter = Time.unscaledTime + 0.4f;
		((UnityEvent<BaseEventData>)(object)entry.callback).AddListener((UnityAction<BaseEventData>)delegate
		{
			if (!(Time.unscaledTime < activateAfter)) callback2();
		});
		trigger.triggers.Add(entry);
	}
}

// ── Video background RenderTexture lifetime ────────────────────────────────────

internal sealed class VisitApiVideoBackground : MonoBehaviour
{
	internal RenderTexture? Rt;

	private void OnDestroy()
	{
		if (!((UnityEngine.Object)(object)Rt == (UnityEngine.Object)null))
		{
			Rt.Release();
			UnityEngine.Object.Destroy((UnityEngine.Object)(object)Rt);
			Rt = null;
		}
	}
}

// ── One-frame deferred visit-tab injection ─────────────────────────────────────

internal sealed class VisitButtonPendingInjector : MonoBehaviour
{
	private Component? _anchor;
	private string? _traderId;
	private bool _armed;
	private int _frameArmed;

	internal void Arm(Component? anchor, string traderId)
	{
		_anchor = anchor;
		_traderId = traderId;
		_armed = true;
		_frameArmed = Time.frameCount;
	}

	internal void Cancel() => _armed = false;

	private void LateUpdate()
	{
		if (_armed && Time.frameCount > _frameArmed)
		{
			_armed = false;
			GameObject val = FindTraderDealScreenChild() ?? ((Component)this).gameObject;
			(val.GetComponent<TraderDealScreenVisitButton>() ?? val.AddComponent<TraderDealScreenVisitButton>()).Refresh(_anchor, _traderId);
		}
	}

	private GameObject? FindTraderDealScreenChild()
	{
		MonoBehaviour[] componentsInChildren = ((Component)this).GetComponentsInChildren<MonoBehaviour>(true);
		foreach (MonoBehaviour val in componentsInChildren)
		{
			if ((UnityEngine.Object)(object)val != (UnityEngine.Object)null && ((object)val).GetType().Name == "TraderDealScreen")
				return ((Component)val).gameObject;
		}
		return null;
	}
}
