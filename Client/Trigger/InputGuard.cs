using EFT;
using UnityEngine;

namespace VisitAPI.Native;

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

    public static void Tick()
    {
        if (_blocked && Time.unscaledTime - _since > 2f && !DialogScreenTracker.Open) Release();
    }
}
