using System.Collections.Generic;
using UnityEngine;

namespace VisitAPI;

/// <summary>
/// 藏身处"拜访"触发器。
/// 仅包含 Hideout 特有逻辑：任务状态门控、节点覆盖、HideoutPlayerOwner 查找与额外档案来源；
/// 与原生交互动作的合并/还原由基类的 MergeWithNativeActions 路径处理。
/// 配置模型见 HideoutTriggerConfig.cs。
/// </summary>
internal sealed class HideoutInteractTrigger : InteractTriggerBase
{
	internal string? NodeOverride;
	internal string? QuestId;
	internal List<string>? ShowWhenStatus;

	private bool _questStatusFetched;

	// Camera.main 每次按 Tag 全场景搜索并分配数组，缓存引用避免每帧 GC
	private Camera? _cachedCamera;

	protected override string LogTag => "[HideoutTrigger]";

	protected override bool MergeWithNativeActions => true;

	// 配置了任务条件的触发器由任务状态门控（状态匹配期间可重复出现），不使用"首次拜访"一次性门控
	protected override bool UseFirstVisitGate => string.IsNullOrEmpty(QuestId);

	// LateUpdate 确保运行在原生藏身处系统的 Update 之后（它在 Update 里写 AvailableInteractionState）
	private void LateUpdate()
	{
		Tick();
	}

	protected override string ResolveStartNode(DialogTree tree)
	{
		return NodeOverride ?? base.ResolveStartNode(tree);
	}

	protected override UnityEngine.Object? FindGpo()
	{
		if (VisitPlugin.CachedHideoutOwner != (UnityEngine.Object)null)
		{
			return VisitPlugin.CachedHideoutOwner;
		}
		System.Type? t = VisitPlugin.HpoType
			?? TraderDealScreenVisitButton.FindType("EFT.HideoutPlayerOwner")
			?? TraderDealScreenVisitButton.FindType("EFT.Hideout.HideoutPlayerOwner");
		return t != null ? UnityEngine.Object.FindObjectOfType(t) : null;
	}

	protected override bool ShouldShowInteraction()
	{
		// 必须真正走近本触发器（情报中心）才注入。藏身处的 AvailableInteractionState 是
		// 全藏身处共享的"当前可交互对象"状态——若不判距离，玩家靠近发电机等其它区域时
		// 会把"拜访"误并进去并替换掉原生交互对象，破坏发电机/灯光/状态（重进才恢复）。
		// 藏身处为第一人称可走动，Camera.main 即玩家视角，距离判断可靠。
		if (!IsPlayerWithinRange())
		{
			return false;
		}
		// 任务状态条件
		if (string.IsNullOrEmpty(QuestId))
		{
			return true;
		}
		if (ShowWhenStatus == null || ShowWhenStatus.Count == 0)
		{
			return false;
		}
		// 首次检查时从服务端拉一次真实状态（缓存默认值不可靠，重启游戏后尤其如此）
		if (!_questStatusFetched && !string.IsNullOrEmpty(ProfileId))
		{
			_questStatusFetched = true;
			QuestStatusCache.BatchFetch(ProfileId, new string[1] { QuestId! });
		}
		return QuestStatusCache.AnyMatches(ShowWhenStatus, QuestStatusCache.GetStatus(QuestId!));
	}

	private bool IsPlayerWithinRange()
	{
		if ((UnityEngine.Object)(object)_cachedCamera == (UnityEngine.Object)null || !_cachedCamera.isActiveAndEnabled)
		{
			_cachedCamera = Camera.main;
			if ((UnityEngine.Object)(object)_cachedCamera == (UnityEngine.Object)null)
			{
				return false;
			}
		}
		return Vector3.Distance(((Component)_cachedCamera).transform.position, TriggerPosition) <= MaxDistance;
	}

	protected override bool TryExtraProfileSources(ref string profileId, ref string playerName)
	{
		string? lastId = NativeQuestController.LastKnownProfileId;
		if (!string.IsNullOrEmpty(lastId))
		{
			profileId = lastId!;
			return true;
		}
		// 兜底：从 EFT 启动命令行 token 读取档案 ID
		return VisitPlugin.TryGetProfileFromCommandLine(out profileId);
	}
}
