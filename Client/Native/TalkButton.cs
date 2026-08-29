using System.Collections;
using System.Linq;
using Comfort.Common;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;
using UnityEngine;
using VisitAPI.Dialog;

namespace VisitAPI.Native;

[HarmonyPatch(typeof(TraderScreensGroup), "SelectTrader")]
public static class TalkButton
{
	private static GameObject _button;

	private static TraderScreensGroup _screen;

	private static bool _opening;

	private static void Postfix(TraderScreensGroup __instance)
	{
		_screen = __instance;
		string traderId = __instance.Trader?.Id;
		DialogTree dialogTree = ((traderId != null && DialogFiles.Loader.TraderIds().Contains(traderId)) ? DialogFiles.Loader.Load(traderId) : null);
		bool canNarrate = dialogTree == null && traderId != null && NarrateEntry.CanVisit(traderId);
		bool showButton = !TabRouter.DialogWindowOpen && (canNarrate || (dialogTree != null && TabPasses(dialogTree, __instance)));
		if (_button == null && showButton)
		{
			_button = TalkButtonUi.Build(__instance, Open);
		}
		if (_button != null)
		{
			_button.SetActive(showButton);
			if (showButton)
			{
				Place(__instance);
			}
		}
	}

	private static bool TabPasses(DialogTree tree, TraderScreensGroup tsg)
	{
		if (tree.TabQuestId == null)
		{
			return true;
		}
		Quest quest = tsg.QuestController?.Quests?.GetConditional(tree.TabQuestId);
		return quest != null && tree.TabStatuses.Contains((int)quest.QuestStatus);
	}

	private static void Place(TraderScreensGroup tsg)
	{
		RectTransform buttonRt = (RectTransform)_button.transform;
		RectTransform parentRt = (RectTransform)buttonRt.parent;
		Vector3[] corners = new Vector3[4];
		((RectTransform)tsg._traderCardsContainer).GetWorldCorners(corners);
		float topGap = parentRt.InverseTransformPoint(corners[1]).y - parentRt.rect.yMax;
		buttonRt.anchoredPosition = new Vector2(Plugin.TalkOffsetX.Value, topGap + Plugin.TalkOffsetY.Value);
	}

	private static void Open()
	{
		if (!_opening && !TabRouter.DialogWindowOpen && Object.FindObjectOfType<TraderDialogScreen>() == null)
		{
			_opening = true;
			Singleton<GUISounds>.Instance.PlayUISound(EUISoundType.ButtonClick);
			string id = _screen.Trader.Id;
			DialogTree dialogTree = (DialogFiles.Loader.TraderIds().Contains(id) ? DialogFiles.Loader.Load(id) : null);
			string error;
			if (dialogTree == null && NarrateEntry.CanVisit(id))
			{
				NarrateEntry.Visit(id);
				Plugin.Instance.StartCoroutine(Rearm());
			}
			else if (dialogTree == null)
			{
				_opening = false;
				Plugin.Log.LogWarning("[talk] no .dlg for " + id);
			}
			else if (!DialogOpener.TryOpen(dialogTree, _screen.Profile, _screen.QuestController, _screen.InventoryController, _screen, out error))
			{
				_opening = false;
				Plugin.Log.LogWarning("[talk] open failed: " + error);
			}
			else
			{
				Plugin.Instance.StartCoroutine(Rearm());
			}
		}
	}

	private static IEnumerator Rearm()
	{
		for (int i = 0; i < 300; i++)
		{
			if (Object.FindObjectOfType<TraderDialogScreen>() != null)
			{
				break;
			}
			yield return null;
		}
		_opening = false;
	}
}
