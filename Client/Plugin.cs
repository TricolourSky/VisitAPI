using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using VisitAPI.Native;

namespace VisitAPI
{
    [BepInPlugin("com.sora.visitapi", "VisitAPI", "0.4.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log = null!;
        internal static Plugin Instance = null!;

        internal static readonly HashSet<string> RegisteredTraders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Language for VisitAPI's own UI/log text (auto = follow EFT's current language).
        internal static ConfigEntry<string> LanguageMode = null!;

        // Out-of-raid 对话 button config (rebuild-free tuning of label + placement on the trade screen).
        internal static ConfigEntry<string> TalkLabel = null!;
        internal static ConfigEntry<int> TalkFontSize = null!;
        internal static ConfigEntry<float> TalkOffsetX = null!;
        internal static ConfigEntry<float> TalkOffsetY = null!;

        private const string SoraId = "90726f6a656374536f726132";

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            Log.LogInfo("VisitAPI 0.4.0 loading (rewrite)");

            LanguageMode = Config.Bind("General", "Language", "auto",
                "VisitAPI 自身文本(UI/日志)的语言: auto=跟随EFT / zh / en  |  Language for VisitAPI's own text: auto (follow EFT) / zh / en");
            string lm = LanguageMode.Value.Trim().ToLowerInvariant();
            Loc.SetMode(lm == "zh" ? Loc.Mode.Zh : lm == "en" ? Loc.Mode.En : Loc.Mode.Auto);

            TalkLabel = Config.Bind("TalkButton", "Label", "",
                "对话按钮文字(留空=按游戏语言自动)  |  'Talk' button label (empty = auto by game language)");
            TalkFontSize = Config.Bind("TalkButton", "FontSize", 24, "按钮文字字号  |  Button font size");
            TalkOffsetX = Config.Bind("TalkButton", "CenterOffsetX", 0f,
                "对话按钮相对屏幕顶部中心的 X 偏移(0=居中,负=左,正=右)  |  'Talk' button X offset from screen top-centre (0=centre, -=left, +=right)");
            TalkOffsetY = Config.Bind("TalkButton", "CenterOffsetY", 0f,
                "Y 偏移(0=与返回同高,负=下移)  |  Y offset (0=level with the close button, negative=lower)");

            // Auto-discover every trader that ships a `<id>.dlg` → whitelist bypass + 对话 button for any modded trader.
            foreach (string id in DialogTreeLoader.ListTraderIds()) RegisteredTraders.Add(id);
            RegisteredTraders.Add(SoraId);
            Log.LogInfo("[VisitAPI] registered " + RegisteredTraders.Count + " trader(s) with .dlg");

            Harmony harmony = new Harmony("com.sora.visitapi");
            FavoriteSchemeGuard.Apply(harmony);

            if (NativeBinder.Bind())
            {
                WhitelistPatch.Apply(harmony);
                DialogUiBinder.Bind();
                OptionRowPatch.Apply(harmony);
                TraderScreenEntryPatch.Apply(harmony);
            }
        }

        private void Update()
        {
            RaidTriggerManager.Tick();

            // F11: print the camera position so .dlg authors can find raid/hideout trigger coordinates.
            if (Input.GetKeyDown(KeyCode.F11))
            {
                Camera c = Camera.main;
                if (c != (UnityEngine.Object)null)
                {
                    Vector3 p = c.transform.position;
                    Log.LogInfo("[Coords] camera position = (" + p.x.ToString("F2") + ", " + p.y.ToString("F2") + ", " + p.z.ToString("F2") + ")  — paste into a hideout/raid trigger");
                }
            }
        }
    }
}
