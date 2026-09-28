using EFT;
using HarmonyLib;

namespace VisitAPI.Native;

public static class NarrateLocationId
{
    public const string Value = "narrate";

    public static void Fill(GameWorld world, string when)
    {
        if (!Narrating.IsVisitWorld(world) || !string.IsNullOrEmpty(world.LocationId)) return;
        world.LocationId = Value;
    }
}

[HarmonyPatch(typeof(GameWorld), nameof(GameWorld.Awake))]
public static class NarrateLocationIdOnAwake
{
    static void Postfix(GameWorld __instance) => NarrateLocationId.Fill(__instance, "on Awake");
}

[HarmonyPatch(typeof(GameWorld), nameof(GameWorld.OnGameStarted))]
public static class NarrateLocationIdOnStart
{
    [HarmonyPriority(Priority.First)]
    static void Prefix(GameWorld __instance) => NarrateLocationId.Fill(__instance, "before OnGameStarted");
}
