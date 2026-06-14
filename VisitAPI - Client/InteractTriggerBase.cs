using System;
using System.Reflection;
using UnityEngine;

namespace VisitAPI;

/// <summary>
/// Raid / Hideout 拜访触发器的共用基类。
/// 统一了原先两个组件里重复的：GPO 反射解析、AvailableInteractionState 注入/清理、
/// 档案提取轮询、FireVisit 打开原生对话的完整流程。
/// 子类只需回答三件事：去哪找 PlayerOwner（FindGpo）、本帧是否显示提示（ShouldShowInteraction）、
/// 是否与原生交互动作合并（MergeWithNativeActions）。
/// </summary>
internal abstract class InteractTriggerBase : MonoBehaviour
{
	internal string TraderId = "";
	internal string PromptText = "拜访";
	internal float MaxDistance = 3f;
	internal Vector3 TriggerPosition;

	protected string ProfileId = "";
	protected string PlayerName = "";
	protected bool VisitFired;

	private bool _profileExtracted;
	private float _nextProfileCheck;
	private float _nextGpoSearch;
	private UnityEngine.Object? _cachedGpo;
	private bool _interactionActive;
	private int _nativeFirstSeenFrame = -1;

	// 反射缓存按实例保存：Raid 与 Hideout 的 PlayerOwner 是不同类型，
	// 原实现里各自用 static 缓存，合并后若仍用 static 会互相污染
	private FieldInfo? _interactionStateField;
	private MethodInfo? _setValueMethod;
	private MethodInfo? _getValueMethod;
	private bool _reflectionResolved;

	protected abstract string LogTag { get; }

	/// <summary>找到承载 AvailableInteractionState 的 PlayerOwner（Raid: GamePlayerOwner；Hideout: HideoutPlayerOwner）。</summary>
	protected abstract UnityEngine.Object? FindGpo();

	/// <summary>本帧是否应显示交互提示（朝向 / 任务状态等子类自有条件）。</summary>
	protected abstract bool ShouldShowInteraction();

	/// <summary>true：把自己的动作合并进原生 ActionsReturnClass（Hideout）；false：整体覆盖（Raid）。</summary>
	protected virtual bool MergeWithNativeActions => false;

	/// <summary>是否使用"首次拜访"一次性门控（已拜访则销毁触发器）。任务状态驱动的触发器应关闭。</summary>
	protected virtual bool UseFirstVisitGate => true;

	/// <summary>对话起始节点；子类可覆盖（如 Hideout 的 NodeOverride）。</summary>
	protected virtual string ResolveStartNode(DialogTree tree)
	{
		return string.IsNullOrEmpty(tree.FirstVisitNode) ? tree.StartNode : tree.FirstVisitNode!;
	}

	/// <summary>子类额外的档案来源（按顺序在通用来源失败后尝试）。返回 true 表示已取得。</summary>
	protected virtual bool TryExtraProfileSources(ref string profileId, ref string playerName)
	{
		return false;
	}

	/// <summary>子类在 Update / LateUpdate 中调用的统一帧逻辑。</summary>
	protected void Tick()
	{
		if (!_profileExtracted)
		{
			TryExtractProfile();
		}
		// 预热：把 FindObjectOfType 全场景扫描和反射解析挪到进入交互范围之前完成
		//（对局初期、被加载等待掩盖时就绪），否则玩家第一次走进范围会因扫描卡顿一下
		if (_cachedGpo == (UnityEngine.Object)null)
		{
			UnityEngine.Object? prewarmGpo = GetOrFindGpo();
			if (prewarmGpo != (UnityEngine.Object)null)
			{
				ResolveReflection(prewarmGpo);
			}
		}
		if (VisitFired || string.IsNullOrEmpty(ProfileId))
		{
			HideInteraction();
			return;
		}
		if (UseFirstVisitGate && !DialogStateStore.IsFirstVisit(TraderId, ProfileId))
		{
			HideInteraction();
			UnityEngine.Object.Destroy((UnityEngine.Object)(object)((Component)this).gameObject);
			return;
		}
		if (ShouldShowInteraction())
		{
			ShowInteraction();
		}
		else
		{
			HideInteraction();
		}
	}

	protected virtual void OnDestroy()
	{
		HideInteraction();
	}

	// ── 交互提示注入 ───────────────────────────────────────────────────────────

	private void ShowInteraction()
	{
		// 覆盖模式注入一次即可，已激活时早退，避免每帧反射 GetValue
		//（合并模式需要每帧对比原生动作列表，不能早退）
		if (_interactionActive && !MergeWithNativeActions)
		{
			return;
		}
		UnityEngine.Object? gpo = GetOrFindGpo();
		if (gpo == (UnityEngine.Object)null || !ResolveReflection(gpo))
		{
			return;
		}
		try
		{
			object? state = _interactionStateField!.GetValue(gpo);
			if (state == null)
			{
				return;
			}
			if (MergeWithNativeActions)
			{
				// 严格以原生触发为门控：原生没设置交互时我们也不设置
				ActionsReturnClass? native = _getValueMethod!.Invoke(state, null) as ActionsReturnClass;
				if (native == null)
				{
					_interactionActive = false;
					_nativeFirstSeenFrame = -1;
					return;
				}
				// 已包含我们的动作时跳过 set_Value，避免每帧重置滚轮选中项
				foreach (ActionsTypesClass a in native.Actions)
				{
					if (a.Name == PromptText)
					{
						_interactionActive = true;
						return;
					}
				}
				// 原生刚建好交互面板的同一帧不做二次注入——同帧两次整面板重建（TMP 生成 + 布局）
				// 是面板弹出瞬间可感知卡顿的来源；推迟到下一帧合并，肉眼无差别
				if (_nativeFirstSeenFrame < 0)
				{
					_nativeFirstSeenFrame = Time.frameCount;
					return;
				}
				if (Time.frameCount == _nativeFirstSeenFrame)
				{
					return;
				}
				_nativeFirstSeenFrame = -1;
				ActionsReturnClass combined = new ActionsReturnClass();
				foreach (ActionsTypesClass a in native.Actions)
				{
					combined.Actions.Add(a);
				}
				combined.Actions.Add(new ActionsTypesClass { Name = PromptText, Action = FireVisit });
				combined.InitSelected();
				_setValueMethod!.Invoke(state, new object[] { combined });
			}
			else
			{
				if (_interactionActive)
				{
					return;
				}
				ActionsReturnClass actions = new ActionsReturnClass();
				actions.Actions.Add(new ActionsTypesClass { Name = PromptText, Action = FireVisit });
				actions.InitSelected();
				_setValueMethod!.Invoke(state, new object[] { actions });
			}
			_interactionActive = true;
		}
		catch (Exception ex)
		{
			VisitPlugin.Log.LogWarning((object)(LogTag + " ShowInteraction failed: " + ex.Message));
		}
	}

	protected void HideInteraction()
	{
		_nativeFirstSeenFrame = -1;
		if (!_interactionActive)
		{
			return;
		}
		_interactionActive = false;
		UnityEngine.Object? gpo = _cachedGpo;
		if (gpo == (UnityEngine.Object)null || _interactionStateField == null || _setValueMethod == null)
		{
			return;
		}
		try
		{
			object? state = _interactionStateField.GetValue(gpo);
			if (state == null)
			{
				return;
			}
			if (MergeWithNativeActions && _getValueMethod != null)
			{
				// 仅移除我们的动作，保留原生动作（新对象触发响应式通知）
				if (!(_getValueMethod.Invoke(state, null) is ActionsReturnClass existing))
				{
					return;
				}
				ActionsReturnClass cleaned = new ActionsReturnClass();
				foreach (ActionsTypesClass a in existing.Actions)
				{
					if (a.Name != PromptText)
					{
						cleaned.Actions.Add(a);
					}
				}
				if (cleaned.Actions.Count > 0)
				{
					cleaned.InitSelected();
				}
				_setValueMethod.Invoke(state, new object[] { cleaned.Actions.Count > 0 ? (object)cleaned : null! });
			}
			else
			{
				_setValueMethod.Invoke(state, new object[1]);
			}
		}
		catch
		{
		}
	}

	private UnityEngine.Object? GetOrFindGpo()
	{
		if (_cachedGpo != (UnityEngine.Object)null)
		{
			return _cachedGpo;
		}
		// FindGpo 内含 FindObjectOfType 全场景扫描，失败时限制每秒最多重试一次
		if (Time.unscaledTime < _nextGpoSearch)
		{
			return null;
		}
		_nextGpoSearch = Time.unscaledTime + 1f;
		_cachedGpo = FindGpo();
		return _cachedGpo;
	}

	private bool ResolveReflection(UnityEngine.Object gpo)
	{
		if (_reflectionResolved)
		{
			return _setValueMethod != null && _getValueMethod != null;
		}
		if (_interactionStateField == null)
		{
			_interactionStateField = gpo.GetType().GetField("AvailableInteractionState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (_interactionStateField == null)
			{
				// 类型上没有该字段：永久失败，锁定不再重试
				_reflectionResolved = true;
				VisitPlugin.Log.LogWarning((object)(LogTag + " AvailableInteractionState field not found"));
				return false;
			}
		}
		object? state = _interactionStateField.GetValue(gpo);
		if (state == null)
		{
			// 对局初期可能尚未初始化：不锁定，下一帧再试
			return false;
		}
		Type stateType = state.GetType();
		_setValueMethod = stateType.GetMethod("set_Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		_getValueMethod = stateType.GetMethod("get_Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		_reflectionResolved = true;
		if (_setValueMethod == null || _getValueMethod == null)
		{
			VisitPlugin.Log.LogWarning((object)(LogTag + " set_Value/get_Value not found on AvailableInteractionState"));
			return false;
		}
		return true;
	}

	// ── 拜访触发 ───────────────────────────────────────────────────────────────

	protected void FireVisit()
	{
		if (VisitFired)
		{
			return;
		}
		VisitFired = true;
		HideInteraction();
		DialogTree? tree = DialogTreeLoader.TryLoad(TraderId);
		if (tree == null)
		{
			VisitPlugin.Log.LogWarning((object)(LogTag + " Dialog tree not found for " + TraderId));
			VisitFired = false;
			return;
		}
		string node = ResolveStartNode(tree);
		DialogStateStore.MarkVisited(TraderId, ProfileId);
		Action onClose = delegate
		{
			UnityEngine.Object.Destroy((UnityEngine.Object)(object)((Component)this).gameObject);
		};
		VisitPlugin.Log.LogInfo((object)(LogTag + " Firing visit for " + TraderId + " → node '" + node + "'"));
		if (!TraderDealScreenVisitButton.TryShowNativeDialogInRaid(TraderId, tree, node, ProfileId, PlayerName, onClose))
		{
			VisitPlugin.Log.LogWarning((object)(LogTag + " Native dialog unavailable"));
			onClose();
		}
	}

	// ── 档案提取（每秒轮询直到成功）────────────────────────────────────────────

	private void TryExtractProfile()
	{
		if (Time.unscaledTime < _nextProfileCheck)
		{
			return;
		}
		_nextProfileCheck = Time.unscaledTime + 1f;
		if (TraderDealScreenVisitButton.TryGetCachedProfile(out string profileId, out string playerName) && !string.IsNullOrEmpty(profileId))
		{
			SetProfile(profileId, playerName, "trader cache");
			return;
		}
		if (VisitPlugin.TryGetProfileInfo(out profileId, out playerName) && !string.IsNullOrEmpty(profileId))
		{
			SetProfile(profileId, playerName, "GameWorld");
			return;
		}
		string extraId = "";
		string extraName = "";
		if (TryExtraProfileSources(ref extraId, ref extraName) && !string.IsNullOrEmpty(extraId))
		{
			SetProfile(extraId, extraName, "extra source");
		}
	}

	private void SetProfile(string profileId, string playerName, string source)
	{
		ProfileId = profileId;
		PlayerName = playerName;
		_profileExtracted = true;
		VisitPlugin.Log.LogInfo((object)(LogTag + " Profile from " + source + ": id='" + profileId + "'"));
	}
}
