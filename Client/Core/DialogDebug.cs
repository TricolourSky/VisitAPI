namespace VisitAPI.Native;

public static class DialogDebug
{
    public static void OnCoordKey()
    {
        var p = EFT.GamePlayerOwner.MyPlayer;
        if (p == null) { Plugin.Log.LogWarning("[coord] no player - stand in the hideout or a raid"); return; }
        var cam = UnityEngine.Camera.main;
        var pos = cam != null ? cam.transform.position : p.Transform.position;
        var loc = Comfort.Common.Singleton<EFT.GameWorld>.Instantiated ? Comfort.Common.Singleton<EFT.GameWorld>.Instance.LocationId : "?";
        Plugin.Log.LogInfo($"[coord] ({pos.x:0.##}, {pos.y:0.##}, {pos.z:0.##})  location={loc}" + (cam == null ? "  (没找到相机，这是脚底坐标)" : ""));
    }
}
