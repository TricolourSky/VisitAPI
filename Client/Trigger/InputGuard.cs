using EFT;
using UnityEngine;

namespace VisitAPI.Native;

/// <summary>战局 / 藏身处里由触发点开的对话屏：开着的时候鼠标不能再转玩家视角（SORA 09-10 实机：对话里一动鼠标人就跟着转头）。
/// 用的是引擎给 NPC 对话准备的开关 <c>GamePlayerOwner.IgnoreInputInNPCDialog</c>——BTR 司机 / 灯塔守卫身边的
/// <c>IgnorePlayerInputZone</c> 进出时开关的就是它：视角轴清零、除 ResetLookDirection 外的指令全拦，UI 点击不受影响。
/// 商人访问（Narrating）不走这条——那边没有玩家视角可转。引擎在 GamePlayerOwner 停止时（离开战局/藏身处）会自己把开关归零。</summary>
public static class InputGuard
{
    static bool _blocked;
    static float _since;

    public static void Block()
    {
        if (_blocked || Narrating.Now || GamePlayerOwner.MyPlayer == null) return;
        GamePlayerOwner.SetIgnoreInputInNPCDialog(true);
        _blocked = true;
        _since = Time.unscaledTime;
        Plugin.Log.LogDebug("[dlg] 玩家视角已锁（对话屏开着）");
    }

    public static void Release()
    {
        if (!_blocked) return;
        GamePlayerOwner.SetIgnoreInputInNPCDialog(false);
        _blocked = false;
        Plugin.Log.LogDebug("[dlg] 玩家视角已放开");
    }

    /// 兜底：对话屏没走 Close 就没了（换图 / 被销毁），别把玩家的视角永远锁死。TriggerHost.Tick 每秒调一次，O(1)。
    /// 开屏是排队（Queued）的，Block 之后要过一两帧 Show 才跑到，所以刚锁上的 2 秒内不判
    public static void Tick()
    {
        if (_blocked && Time.unscaledTime - _since > 2f && !DialogScreenTracker.Open) Release();
    }
}
