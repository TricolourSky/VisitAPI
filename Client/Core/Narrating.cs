using System;
using Comfort.Common;
using EFT;

namespace VisitAPI.Native;

public static class Narrating
{
    public const string WorldObjectName = "NarrateWorld";

    public static bool Now =>
        (TarkovApplication.Exist(out var app) && app.NarrateControllerAccess != null && app.NarrateControllerAccess.GameExist)
        || (Singleton<GameWorld>.Instantiated && IsVisitWorld(Singleton<GameWorld>.Instance));

    public static bool IsVisitWorld(GameWorld world)
    {
        if (world == null) return false;
        if (world is NarrateGameWorld) return true;
        if (TarkovApplication.Exist(out var app) && app.NarrateControllerAccess != null && ReferenceEquals(app.NarrateControllerAccess._gameWorld, world)) return true;
        try { return world.name == WorldObjectName; }
        catch { return false; }
    }
}

public static class Raid
{
    public static bool Now => Singleton<AbstractGame>.Instantiated && Singleton<AbstractGame>.Instance.InRaid;

    public static bool IsHideout(string locationId) => (locationId ?? "").IndexOf("hideout", StringComparison.OrdinalIgnoreCase) >= 0;
}
