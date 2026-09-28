namespace VisitAPI.Native;

/// <summary>09-27 SORA：改回按 F11 取坐标（1.3.4 整理设置页时连同 Debug.CoordKey 一起拆了，作者没法再量触发点）。
/// 固定 F11、不进设置页；写一行日志，编辑器帮助页教作者照抄：括号里是相机坐标（触发点判距离用的就是 Camera.main，同一个基准），
/// location= 后面是战局触发点该填的地图 id。找不到相机才退回脚底坐标并注明。</summary>
public static class CoordKey
{
    public static void Print()
    {
        var p = EFT.GamePlayerOwner.MyPlayer;
        if (p == null) { Plugin.Log.LogWarning("[coord] no player - stand in the hideout or a raid"); return; }
        var cam = UnityEngine.Camera.main;
        var pos = cam != null ? cam.transform.position : p.Transform.position;
        var loc = Comfort.Common.Singleton<EFT.GameWorld>.Instantiated ? Comfort.Common.Singleton<EFT.GameWorld>.Instance.LocationId : "?";
        Plugin.Log.LogInfo($"[coord] ({pos.x:0.##}, {pos.y:0.##}, {pos.z:0.##})  location={loc}" + (cam == null ? "  (no camera found, these are the feet coordinates)" : ""));
    }
}
