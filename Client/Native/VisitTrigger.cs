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

	private GamePlayerOwner _owner;

	private float _cooldown;

	private bool _shown;

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
		if (RequireLook && Vector3.Angle(main.transform.forward, point - main.transform.position) > Mathf.Clamp(Mathf.Atan2(Data.Radius, Mathf.Max(distance, 0.5f)) * Mathf.Rad2Deg, 8f, 40f))
		{
			return false;
		}
		return GatePasses();
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
		TriggerMenu.Hide(_owner, Prompt());
		_shown = false;
		_cooldown = Time.unscaledTime + 1.5f;
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

	private void OnDestroy()
	{
		if (_shown && _owner != null)
		{
			TriggerMenu.Hide(_owner, Prompt());
		}
	}
}
