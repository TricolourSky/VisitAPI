using System.Collections;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Dialogs;
using EFT.HealthSystem;
using EFT.Quests;
using EFT.UI;
using EFT.UI.Screens;
using HarmonyLib;

namespace VisitAPI.Native;

public static class NarrateTabs
{
	private static readonly System.Reflection.FieldInfo MenuOpField = AccessTools.Field(typeof(TarkovApplication), "_menuOperation");
	private static BaseTraderDialogController _watched;
	private static string _traderId;

	public static void Watch(TarkovApplication app, string traderId)
	{
		_traderId = traderId;
		if (!(MenuOpField?.GetValue(app) is MainMenuShowOperation op) || op.DialogController == null)
		{
			return;
		}
		if (_watched != null)
		{
			_watched.OnActionFinished -= Handle;
		}
		_watched = op.DialogController;
		_watched.OnActionFinished -= Handle;
		_watched.OnActionFinished += Handle;
	}

	private static void Handle(DialogAction action)
	{
		if (action is DialogQuestsScreenAction)
		{
			Plugin.Instance.StartCoroutine(OpenTasks(_traderId));
		}
	}

	private static IEnumerator OpenTasks(string traderId)
	{
		yield return null;
		if (!TarkovApplication.Exist(out var app) || app.Session == null || !(MenuOpField?.GetValue(app) is MainMenuShowOperation op))
		{
			yield break;
		}
		var trader = app.Session.Traders.FirstOrDefault(t => t.Id == traderId);
		if (trader == null)
		{
			Plugin.Log.LogWarning("[narrate] tasks: trader not in session " + traderId);
			yield break;
		}
		var profile = app.Session.Profile;
		new TraderScreensGroup.TraderScreenController(trader, new[] { trader }, profile, op.InventoryController,
			op.HealthController, op.QuestController, op.achievementsController, app.Session)
			.ShowScreen(EScreenState.Queued);
		var tsg = MonoBehaviourSingleton<MenuUI>.Instance != null ? MonoBehaviourSingleton<MenuUI>.Instance.TraderScreensGroup : null;
		for (var i = 0; tsg != null && i < 60 && !tsg.isActiveAndEnabled; i++)
		{
			yield return null;
		}
		yield return null;
		if (tsg != null && tsg.isActiveAndEnabled)
		{
			tsg.SetMode(TraderScreensGroup.ETraderMode.Tasks);
			Plugin.Log.LogDebug("[narrate] tasks screen opened for " + traderId);
		}
	}
}
