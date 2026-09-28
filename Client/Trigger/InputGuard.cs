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
    }

    public static void Release()
    {
        if (!_blocked) return;
        GamePlayerOwner.SetIgnoreInputInNPCDialog(false);
        _blocked = false;
    }

    public static void Tick()
    {
        if (_blocked && Time.unscaledTime - _since > 2f && !DialogScreenTracker.Open) Release();
    }
}
