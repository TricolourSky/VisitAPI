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

	public bool Auto => Data.Auto || Data.Enter >= 0f;

	private GamePlayerOwner _owner;

	private float _cooldown;

	private bool _shown;

	private bool _fired;

	private float _armed;

	private int _misses;

	private float _nearLog;

	private static QuestController Quests => GamePlayerOwner.MyPlayer?.QuestController ?? Singleton<HideoutRepresentation>.Instance?._questController;

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
			// 走近了但还没够：每 2 秒报一次实际距离。坐标填错/dist 太小是最难自己发现的，
			// 现场什么都不会发生，日志里这一行就是唯一的线索。
			if (!_fired && Data.Enter < 0f && Camera.main != null && Time.unscaledTime >= _nearLog)
			{
				float d = Vector3.Distance(Camera.main.transform.position, new Vector3(Data.X, Data.Y, Data.Z));
				if (d <= Data.Dist * 5f)
				{
					_nearLog = Time.unscaledTime + 2f;
					Plugin.Log.LogInfo($"[trigger] 距触发点 {d:F1}m（需要 ≤{Data.Dist}m）@ ({Data.X}, {Data.Y}, {Data.Z})");
				}
			}
			if (!_fired && GamePlayerOwner.MyPlayer != null && Time.unscaledTime >= _cooldown && ShouldShow())
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
		Quest quest = Quests?.Quests?.GetConditional(Data.IfQuestId);
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
		// accept / finish / fail 可以同时写；三者都没有才去开对话
		var acted = false;
		Plugin.Log.LogInfo($"[trigger] 触发：accept={Data.AcceptId ?? "-"} finish={Data.FinishId ?? "-"} fail={Data.FailId ?? "-"} node={Data.Node ?? "-"}");
		if (Data.AcceptId != null) { AcceptQuest(); acted = true; }
		if (Data.FinishId != null) { SetStatus(Data.FinishId, EQuestStatus.Success); acted = true; }
		if (Data.FailId != null) { SetStatus(Data.FailId, EQuestStatus.Fail); acted = true; }
		if (acted)
		{
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
		var quests = Quests;
		Quest quest = quests?.Quests?.GetConditional(Data.AcceptId);
		if (quest == null)
		{
			// 玩家/任务书可能还没就绪(战局刚载入)，别把 auto 点永久锁死——退回去等冷却后重试；20 次还没有就是 id 写错/前置没到，别刷屏
			if (++_misses <= 20) _fired = false;
			if (_misses == 1 || _misses == 20) Plugin.Log.LogWarning("[trigger] quest not found: " + Data.AcceptId + (_misses == 20 ? " (giving up)" : ""));
			return;
		}
		if (quest.QuestStatus != EQuestStatus.AvailableForStart)
		{
			Plugin.Log.LogInfo($"[trigger] {Data.AcceptId} 现在是 {quest.QuestStatus}，不是「可接」，没接");
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

	// 走到/进图就把某条任务判成完成或失败（剧情用）。没接过的任务先接下再改，否则引擎不认这个状态迁移。
	// 状态写入和对话里的 setstatus: 同一条路（TryExecuteTransition 不行就 SetConditionalStatus 硬写）。
	private void SetStatus(string questId, EQuestStatus want)
	{
		var quests = Quests;
		Quest quest = quests?.Quests?.GetConditional(questId);
		if (quest == null)
		{
			if (++_misses <= 20) _fired = false;
			if (_misses == 1 || _misses == 20) Plugin.Log.LogWarning("[trigger] quest not found: " + questId + (_misses == 20 ? " (giving up)" : ""));
			return;
		}
		if (quest.QuestStatus == want || quest.QuestStatus == EQuestStatus.Success || quest.QuestStatus == EQuestStatus.Fail || quest.QuestStatus == EQuestStatus.MarkedAsFailed)
		{
			Plugin.Log.LogInfo($"[trigger] {questId} 已经是 {quest.QuestStatus}，不动它");
			return;
		}
		if (quest.QuestStatus == EQuestStatus.AvailableForStart) quests.SetConditionalStatus(quest, EQuestStatus.Started);   // 没接过的先接下
		if (want == EQuestStatus.Success)
		{
			// ⚠️ 引擎不认 Started → Success 这一跳（实机实证：状态原地不动）。必须先落到「可提交」，
			// 再走 FinishQuest —— 那才是真交任务：发奖励、发邮件、同步服务端。
			// SetConditionalStatus 只是本地改个数字，出了战局就没了。
			if (quest.QuestStatus < EQuestStatus.AvailableForFinish) quests.SetConditionalStatus(quest, EQuestStatus.AvailableForFinish);
			StartCoroutine(FinishLater(quests, quest));
			return;
		}
		if (!quests.TryExecuteTransition(quest, want)) quests.SetConditionalStatus(quest, want);
		Plugin.Log.LogInfo($"[trigger] {questId} -> {want}（实际变成 {quest.QuestStatus}）");
	}

	// 交任务要等一帧再发，别在引擎的事件派发里改状态（和 ChapterChain 同一条铁律）
	private System.Collections.IEnumerator FinishLater(QuestController quests, Quest quest)
	{
		yield return null;
		var task = quests.FinishQuest(quest, runNetworkTransaction: true);
		while (task != null && !task.IsCompleted) yield return null;
		if (task != null && task.IsFaulted) Plugin.Log.LogWarning("[trigger] finish 失败: " + task.Exception?.GetBaseException().Message);
		else Plugin.Log.LogInfo($"[trigger] {quest.Id} 交任务完成（现在是 {quest.QuestStatus}）");
	}

	private void OnDestroy()
	{
		if (_shown && _owner != null)
		{
			TriggerMenu.Hide(_owner, Prompt());
		}
	}
}
