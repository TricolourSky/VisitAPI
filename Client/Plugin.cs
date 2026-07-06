using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using VisitAPI.Native;
using VisitAPI.Scene;
using VisitAPI.Scene.RetailReplay;

namespace VisitAPI
{
    [BepInPlugin("com.sora.visitapi", "VisitAPI", "0.5.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log = null!;
        internal static Plugin Instance = null!;

        internal static readonly HashSet<string> RegisteredTraders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Language for VisitAPI's own UI/log text (auto = follow EFT's current language).
        internal static ConfigEntry<string> LanguageMode = null!;

        // Out-of-raid button placement (rebuild-free X/Y offset on the trade screen — escape hatch for
        // when another UI mod overlaps the button).
        internal static ConfigEntry<float> TalkOffsetX = null!;
        internal static ConfigEntry<float> TalkOffsetY = null!;

        // 3D vendor-scene visits (retail replay): asset root.
        internal static ConfigEntry<string> SceneAssetsRoot = null!;

        // Optional EFT camera exposure + vignette grading for staged scenes (off by default).
        internal static ConfigEntry<bool> SceneCameraPostFx = null!;

        private const string SoraId = "90726f6a656374536f726132";

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            Log.LogInfo("VisitAPI 0.5.0 loading (dialogue framework + 3D vendor-scene visits)");

            LanguageMode = Config.Bind("General", "Language", "auto",
                "VisitAPI 自身文本(UI/日志)的语言: auto=跟随EFT / zh / en  |  Language for VisitAPI's own text: auto (follow EFT) / zh / en");
            string lm = LanguageMode.Value.Trim().ToLowerInvariant();
            Loc.SetMode(lm == "zh" ? Loc.Mode.Zh : lm == "en" ? Loc.Mode.En : Loc.Mode.Auto);

            TalkOffsetX = Config.Bind("TalkButton", "CenterOffsetX", 0f,
                "对话按钮相对屏幕顶部中心的 X 偏移(0=居中,负=左,正=右)  |  'Talk' button X offset from screen top-centre (0=centre, -=left, +=right)");
            TalkOffsetY = Config.Bind("TalkButton", "CenterOffsetY", 0f,
                "Y 偏移(0=与返回同高,负=下移)  |  Y offset (0=level with the close button, negative=lower)");

            SceneAssetsRoot = Config.Bind("Scene", "AssetsRoot", "",
                "商人3D场景资源根目录(含 tradermod.shared.dll 和 bundles\\vendors；留空=自动探测)  |  Vendor-scene assets root (holds tradermod.shared.dll + bundles\\vendors; empty = auto-probe)");
            SceneCameraPostFx = Config.Bind("Scene", "CameraPostFx", false,
                "启用EFT相机曝光+暗角(更接近零售色调;下次打开场景生效)  |  Enable EFT camera exposure + vignette (closer to retail tone; applies on next scene open)");

            // Auto-discover every trader that ships a `<id>.dlg` → whitelist bypass + 对话 button for any modded trader.
            foreach (string id in DialogTreeLoader.ListTraderIds()) RegisteredTraders.Add(id);
            RegisteredTraders.Add(SoraId);
            Log.LogInfo("[VisitAPI] registered " + RegisteredTraders.Count + " trader(s) with .dlg");

            Harmony harmony = new Harmony("com.sora.visitapi");
            FavoriteSchemeGuard.Apply(harmony);
            RetailPatches.Apply(harmony);

            if (NativeBinder.Bind())
            {
                WhitelistPatch.Apply(harmony);
                DialogUiBinder.Bind();
                OptionRowPatch.Apply(harmony);
                TraderScreenEntryPatch.Apply(harmony);
            }

            SceneAssets.Resolve(SceneAssetsRoot.Value);
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
