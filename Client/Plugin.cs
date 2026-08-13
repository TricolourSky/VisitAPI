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
	public const string Version = "1.0.0";

	public static Plugin Instance;

	public static ManualLogSource Log;

	public static ConfigEntry<string> Language;

	public static ConfigEntry<float> TalkOffsetX;

	public static ConfigEntry<float> TalkOffsetY;

	private void Awake()
	{
		Instance = this;
		Log = base.Logger;
		Language = base.Config.Bind("General", "Language", "auto", "界面语言 | UI language: auto / zh / en");
		TalkOffsetX = base.Config.Bind("TalkButton", "OffsetX", 0f, "访问按钮水平偏移 | Visit button X offset");
		TalkOffsetY = base.Config.Bind("TalkButton", "OffsetY", 0f, "访问按钮垂直偏移 | Visit button Y offset");
		Loc.Mode = Language.Value;
		Language.SettingChanged += delegate
		{
			Loc.Mode = Language.Value;
		};
		Loc.GameCulture = () => LocalizationManager.Instance?.Culture;
		DlgLoc.Picker = Loc.Pick;
		new Harmony("com.sora.visitapi").PatchAll();
		Log.LogInfo($"VisitAPI {Version} loaded (SPT 4.1.1)");
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
