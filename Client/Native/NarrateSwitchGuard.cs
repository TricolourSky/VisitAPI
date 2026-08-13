using Comfort.Common;
using EFT;
using EFT.Dialogs;
using HarmonyLib;

namespace VisitAPI.Native;

// 1.0 零售数据里存在悬空的 SwitchDialog 目标(如 Skier 的 68c2afd6..., 模板只在正式服服务端库里)——
// 原生 method_0 对缺失模板直接炸且异常被点击管线吞掉, 表现为对话转圈死锁。缺失时改道回商人主对话入口。
[HarmonyPatch(typeof(BaseTraderDialogController), "method_0")]
public static class NarrateSwitchGuard
{
	private static void Prefix(BaseTraderDialogController __instance, ref MongoID dialogId, ref MongoID? startingPoint)
	{
		var storage = DialogStorage.Instance;
		if (storage == null || storage.TryGetTemplate(dialogId, out _))
		{
			return;
		}
		Plugin.Log.LogWarning("[narrate] dialog template missing: " + dialogId + " - rerouting to main dialog");
		var traderId = __instance.Trader?.Id;
		GlobalConfiguration.TraderSettings settings = null;
		if (!string.IsNullOrEmpty(traderId))
		{
			Singleton<GlobalConfiguration>.Instance?.TradersSettings?.TryGetValue(traderId, out settings);
		}
		var main = settings?.MainDialog;
		if (main.HasValue && storage.TryGetTemplate(main.Value, out var template))
		{
			dialogId = main.Value;
			startingPoint = null;
			__instance.SetVariableValue(new DialogSetVariableAction.SaveStateData(template.MainVariable, 0));
		}
	}
}
