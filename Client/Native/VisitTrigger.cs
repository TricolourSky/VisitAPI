using Comfort.Common;
using EFT;
using EFT.Hideout;
using EFT.Quests;
using UnityEngine;
using VisitAPI.Dialog;

namespace VisitAPI.Native;

public class VisitTrigger : MonoBehaviour
{
	public string TraderId;

	public DialogTrigger Data;

	public bool Merge;

	public bool RequireLook;

	public bool Auto;

	private GamePlayerOwner _owner;

	private float _cooldown;

	private bool _shown;

	private bool _fired;

	private float _armed;

	private void Update()
	{
		if (Singleton<GameWorld>.Instantiated && Singleton<GameWorld>.Instance is NarrateGameWorld)
		{
			if (_shown && _owner != null)
			{
				TriggerMenu.Hide(_owner, Prompt());
				_shown = false;
			}
			return;
		}
		if (Auto)
		{
			if (!_fired && Time.unscaledTime >= _cooldown && ShouldShow())
			{
				_fired = true;
				Fire();
			}
			return;
		}
		if (_owner == null)
		{
			GamePlayerOwner[] owners = Object.FindObjectsOfType<GamePlayerOwner>();
			foreach (GamePlayerOwner gamePlayerOwner in owners)
			{
				if (!(gamePlayerOwner is NarratePlayerOwner))
				{
					_owner = gamePlayerOwner;
					break;
				}
			}
			if (_owner == null)
			{
				return;
			}
		}
		if (!(Time.unscaledTime < _cooldown))
		{
			if (ShouldShow())
			{
				_shown = TriggerMenu.Show(_owner, Prompt(), Fire, Merge);
			}
			else if (_shown)
			{
				TriggerMenu.Hide(_owner, Prompt());
				_shown = false;
			}
		}
	}

	private bool ShouldShow()
	{
		if (Data.Enter >= 0f)
		{
			return EnterDue() && GatePasses();
		}
		Camera main = Camera.main;
		if (main == null)
		{
			return false;
		}
		Vector3 point = new Vector3(Data.X, Data.Y, Data.Z);
		float distance = Vector3.Distance(main.transform.position, point);
		if (distance > Data.Dist)
		{
			return false;
		}
		if (RequireLook && !LookPasses(main, point))
		{
			return false;
		}
		return GatePasses();
	}

	// 进图计时型触发点：起表点是"玩家真正可控"那一刻(MyPlayer 就位)，
	// 不是 GameWorld 生成那一刻——否则读条阶段就把秒数烧完了，落地即触发。
	private bool EnterDue()
	{
		if (GamePlayerOwner.MyPlayer == null)
		{
			return false;
		}
		if (_armed <= 0f)
		{
			_armed = Time.unscaledTime;
		}
		return Time.unscaledTime - _armed >= Data.Enter;
	}

	// 触发点坐标来自 F11，打印的是玩家脚底(Transform.position)，比摄像机低约 1.6m。
	// 近距离时这段高度差会把视线角推到远大于视角锥（站在点正上方时接近 90°），
	// 逼得玩家非低头不可 → 朝向只比水平分量，高低差交给 dist 距离门槛去挡。
	private bool LookPasses(Camera cam, Vector3 point)
	{
		Vector3 flat = point - cam.transform.position;
		flat.y = 0f;
		if (flat.sqrMagnitude < 0.04f)
		{
			return true;
		}
		Vector3 forward = cam.transform.forward;
		forward.y = 0f;
		return Vector3.Angle(forward, flat) <= Mathf.Clamp(Mathf.Atan2(Data.Radius, Mathf.Max(flat.magnitude, 0.5f)) * Mathf.Rad2Deg, 8f, 40f);
	}

	private bool GatePasses()
	{
		if (Data.IfQuestId == null)
		{
			return true;
		}
		Quest quest = (GamePlayerOwner.MyPlayer?.QuestController ?? Singleton<HideoutRepresentation>.Instance?._questController)?.Quests?.GetConditional(Data.IfQuestId);
		return quest != null && Data.IfStatuses.Contains((int)quest.QuestStatus);
	}

	private string Prompt()
	{
		return string.IsNullOrEmpty(Data.Prompt) ? Loc.Pick("对话", "Talk") : Data.Prompt;
	}

	private void Fire()
	{
		if (_owner != null)
		{
			TriggerMenu.Hide(_owner, Prompt());
		}
		_shown = false;
		_cooldown = Time.unscaledTime + 1.5f;
		if (Data.AcceptId != null)
		{
			AcceptQuest();
			return;
		}
		DialogTree dialogTree = DialogFiles.Loader.Load(TraderId);
		string error;
		if (dialogTree == null)
		{
			Plugin.Log.LogWarning("[trigger] no .dlg for " + TraderId);
		}
		else if (!DialogOpener.TryOpenTriggered(dialogTree, Data.Node, out error))
		{
			Plugin.Log.LogWarning("[trigger] open failed: " + error);
		}
	}

	// 战局内 AcceptQuest 的原生实现就是本地 SetConditionalStatus(Started)，不发网络事务；
	// 藏身处走的 ClientGame 版本才是真事务。两边都用它，行为交给引擎判断。
	private void AcceptQuest()
	{
		var quests = GamePlayerOwner.MyPlayer?.QuestController ?? Singleton<HideoutRepresentation>.Instance?._questController;
		Quest quest = quests?.Quests?.GetConditional(Data.AcceptId);
		if (quest == null)
		{
			// 玩家/任务书可能还没就绪(战局刚载入)，别把 auto 点永久锁死——退回去等冷却后重试
			_fired = false;
			Plugin.Log.LogWarning("[trigger] quest not found: " + Data.AcceptId);
			return;
		}
		quests.AcceptQuest(quest, runNetworkTransaction: true).ContinueWith(t =>
		{
			if (t.IsFaulted)
			{
				Plugin.Log.LogWarning("[trigger] accept failed: " + t.Exception?.GetBaseException().Message);
			}
		});
		Plugin.Log.LogDebug($"[trigger] auto-accepted {Data.AcceptId} -> {quest.QuestStatus}");
	}

	private void OnDestroy()
	{
		if (_shown && _owner != null)
		{
			TriggerMenu.Hide(_owner, Prompt());
		}
	}
}
