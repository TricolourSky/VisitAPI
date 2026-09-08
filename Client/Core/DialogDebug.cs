namespace VisitAPI.Native;

public static class DialogDebug
{
    /// 配置项 Debug.CoordKey 绑的键（默认不绑，09-07 终审：不再写死 F11）
    public static void OnCoordKey()
    {
        var p = EFT.GamePlayerOwner.MyPlayer;
        if (p == null) { Plugin.Log.LogWarning("[coord] no player - stand in the hideout or a raid"); return; }
        // T-2：触发器判距用的是相机（眼睛）位置——打点就打相机坐标，抄进 .dlg 的 dist 不用再脑补 1.6m 高度差
        var cam = UnityEngine.Camera.main;
        var pos = cam != null ? cam.transform.position : p.Transform.position;
        var loc = Comfort.Common.Singleton<EFT.GameWorld>.Instantiated ? Comfort.Common.Singleton<EFT.GameWorld>.Instance.LocationId : "?";
        Plugin.Log.LogInfo($"[coord] ({pos.x:0.##}, {pos.y:0.##}, {pos.z:0.##})  location={loc}" + (cam == null ? "  (没找到相机，这是脚底坐标)" : ""));
    }
}
