using System;
using Comfort.Common;
using EFT;

namespace VisitAPI.Native;

public static class Narrating
{
    public static bool Now =>
        (TarkovApplication.Exist(out var app) && app.NarrateControllerAccess != null && app.NarrateControllerAccess.GameExist)
        || (Singleton<GameWorld>.Instantiated && Singleton<GameWorld>.Instance is NarrateGameWorld);
}

public static class Raid
{
    public static bool Now => Singleton<AbstractGame>.Instantiated && Singleton<AbstractGame>.Instance.InRaid;

    public static bool IsHideout(string locationId) => (locationId ?? "").IndexOf("hideout", StringComparison.OrdinalIgnoreCase) >= 0;
}
