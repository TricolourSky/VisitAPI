using HarmonyLib;
using UnityEngine;

namespace VisitAPI;

/// <summary>
/// 战局内"拜访"触发器。
/// 仅包含 Raid 特有逻辑：门型碰撞箱与玩家朝向判定；其余复用 InteractTriggerBase。
/// </summary>
internal sealed class RaidInteractTrigger : InteractTriggerBase
{
	internal float HitRadius = 1.2f;
	internal float DoorWidth;
	internal float DoorHeight = 2.2f;
	internal float DoorRotationY;

	private Collider? _doorCollider;
	private Camera? _cachedCamera;
	private static System.Type? _s_gpoType;

	protected override string LogTag => "[RaidTrigger]";

	private void Start()
	{
		if (DoorWidth > 0f)
		{
			((Component)this).transform.position = TriggerPosition;
			((Component)this).transform.rotation = Quaternion.Euler(0f, DoorRotationY, 0f);
			BoxCollider box = ((Component)this).gameObject.AddComponent<BoxCollider>();
			box.center = Vector3.zero;
			box.size = new Vector3(DoorWidth, DoorHeight, 0.15f);
			((Collider)box).isTrigger = false;
			_doorCollider = (Collider?)(object)box;
			VisitPlugin.Log.LogInfo((object)$"[RaidTrigger] Door BoxCollider {DoorWidth}m×{DoorHeight}m rotY={DoorRotationY}°");
		}
	}

	private void Update()
	{
		Tick();
	}

	protected override UnityEngine.Object? FindGpo()
	{
		if ((object)_s_gpoType == null)
		{
			_s_gpoType = AccessTools.TypeByName("EFT.GamePlayerOwner");
		}
		return _s_gpoType != null ? UnityEngine.Object.FindObjectOfType(_s_gpoType) : null;
	}

	protected override bool ShouldShowInteraction()
	{
		// Camera.main 每次调用都按 Tag 全场景搜索并分配数组，缓存引用避免每帧 GC 压力
		if ((UnityEngine.Object)(object)_cachedCamera == (UnityEngine.Object)null || !_cachedCamera.isActiveAndEnabled)
		{
			_cachedCamera = Camera.main;
			if ((UnityEngine.Object)(object)_cachedCamera == (UnityEngine.Object)null)
			{
				return false;
			}
		}
		Camera main = _cachedCamera;
		float distance = Vector3.Distance(((Component)main).transform.position, TriggerPosition);
		if (distance > MaxDistance)
		{
			return false;
		}
		if ((UnityEngine.Object)(object)_doorCollider != (UnityEngine.Object)null)
		{
			Ray ray = new Ray(((Component)main).transform.position, ((Component)main).transform.forward);
			return _doorCollider.Raycast(ray, out RaycastHit _, MaxDistance + 1f);
		}
		Vector3 toTrigger = TriggerPosition - ((Component)main).transform.position;
		float angle = Vector3.Angle(((Component)main).transform.forward, toTrigger.normalized);
		float maxAngle = Mathf.Clamp(Mathf.Atan2(HitRadius, Mathf.Max(distance, 0.5f)) * 57.29578f, 8f, 40f);
		return angle <= maxAngle;
	}
}
