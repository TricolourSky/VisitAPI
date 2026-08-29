namespace VisitAPI.Native;

public static class DialogDebug
{
    public static void OnF11()
    {
        var p = EFT.GamePlayerOwner.MyPlayer;
        if (p == null) { Plugin.Log.LogWarning("[F11] no player - stand in the hideout or a raid"); return; }
        var pos = p.Transform.position;
        var loc = Comfort.Common.Singleton<EFT.GameWorld>.Instantiated ? Comfort.Common.Singleton<EFT.GameWorld>.Instance.LocationId : "?";
        Plugin.Log.LogInfo($"[F11] ({pos.x:0.##}, {pos.y:0.##}, {pos.z:0.##})  location={loc}");
    }
}
