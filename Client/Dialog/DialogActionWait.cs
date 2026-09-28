using System;
using System.Threading.Tasks;
using EFT.Dialogs;
using HarmonyLib;

namespace VisitAPI.Native;

/// <summary>1.3.4 B5：原生对话里的接 / 交任务、上交物品、购买服务，引擎本该等动作做完再建下一段对话（ExecuteLine 里 `await task_0`），
/// 但 0.16.9 的 ClientDialogController.CG_ExecuteDialogAction 判断写反了：task_0 为空或已完成时打一条 "Action task is not completed" 把新任务丢掉，
/// 只有旧任务还没完成时才存（IL 核过）。task_0 初始为空，所以永远存不上，下一段对话按动作之前的任务状态建——BSG 日志里那一串 Error 就是它。
/// 这里把那一行判断改对：新动作存进 task_0，让引擎自己等。旧动作还在跑（同一行挂了两个动作）就两个一起等。
/// 存进去的是「兜住异常、最多等 30 秒」的包装：引擎 await 它，动作抛异常或网络迟迟不回都不能把整段对话卡死。</summary>
[HarmonyPatch(typeof(ClientDialogController), nameof(ClientDialogController.CG_ExecuteDialogAction))]
public static class DialogActionWait
{
    const int TimeoutMs = 30000;

    static bool Prefix(ClientDialogController __instance, Task task)
    {
        if (task == null) return false;
        try
        {
            var prev = __instance.task_0;
            var both = prev != null && !prev.IsCompleted ? Task.WhenAll(prev, task) : task;
            __instance.task_0 = Guard(both);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[dlg] could not register dialog action (it will not be awaited, dialog continues): " + e.Message);
        }
        return false;
    }

    static async Task Guard(Task action)
    {
        try
        {
            var done = await Task.WhenAny(action, Task.Delay(TimeoutMs));
            if (done != action) { Plugin.Log.LogWarning($"[dlg] dialog action not finished after {TimeoutMs / 1000}s, no longer waiting (a late result still applies)"); return; }
            await action;
        }
        catch (Exception e) { Plugin.Log.LogWarning("[dlg] dialog action failed (dialog continues): " + e.GetBaseException().Message); }
    }
}
