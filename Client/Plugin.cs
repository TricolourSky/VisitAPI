using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT;
using HarmonyLib;
using UnityEngine;
using VisitAPI.Dialog;
using VisitAPI.Native;

namespace VisitAPI;

[BepInPlugin("com.sora.visitapi", "VisitAPI", Plugin.Version)]
public class Plugin : BaseUnityPlugin
{
	public const string Version = "1.2.0";

	public static Plugin Instance;

	public static ManualLogSource Log;

	public static ConfigEntry<string> Language;

	public static ConfigEntry<float> TalkOffsetX;

	public static ConfigEntry<float> TalkOffsetY;

	public static ConfigEntry<bool> HideStory;

	public static ConfigEntry<bool> ShowUnstarted;

	private void Awake()
	{
		Instance = this;
		Log = base.Logger;
		Language = base.Config.Bind("General", "Language", "auto", "界面语言 | UI language: auto / zh / en");
		TalkOffsetX = base.Config.Bind("TalkButton", "OffsetX", 0f, "访问按钮水平偏移 | Visit button X offset");
		TalkOffsetY = base.Config.Bind("TalkButton", "OffsetY", 0f, "访问按钮垂直偏移 | Visit button Y offset");
		HideStory = base.Config.Bind("Chapter", "HideStoryQuestsInLists", true, "剧情任务（章节及其子任务）只住「剧情」页，不进支线/商人任务列表 | Story quests live only on the STORY tab");
		ShowUnstarted = base.Config.Bind("Chapter", "ShowUnstartedChapters", false, "还没开始的章节也列在「剧情」页上（灰着、标「未开放」）| List chapters that have not started yet on the STORY tab");
		Loc.Mode = Language.Value;
		Language.SettingChanged += delegate
		{
			Loc.Mode = Language.Value;
		};
		Loc.GameCulture = () => LocalizationManager.Instance?.Culture;
		DlgLoc.Picker = Loc.Pick;
		new Harmony("com.sora.visitapi").PatchAll();
		QuestFlags.Prefetch();
		Log.LogInfo($"VisitAPI {Version} loaded (SPT 4.1.3)");
	}

	private void Update()
	{
		if (Input.GetKeyDown(KeyCode.F11))
		{
			DialogDebug.OnF11();
		}
		if (Input.GetKeyDown(KeyCode.F9))
		{
			Log.LogWarning("[narrate] F9 pressed");
			NarrateEntry.Abort();
		}
		TriggerManager.Tick();
	}
}
