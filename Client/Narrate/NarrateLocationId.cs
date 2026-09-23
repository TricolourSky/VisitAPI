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
        Plugin.Log.LogInfo($"[narrate] 访问世界（{world.GetType().Name}）的 LocationId 原生为空，{when}填成 '{Value}'（按地图查表的模组会当成没这张图跳过，不再拿 null 当 key 炸）");
    }
}

[HarmonyPatch(typeof(GameWorld), nameof(GameWorld.Awake))]
public static class NarrateLocationIdOnAwake
{
    static void Postfix(GameWorld __instance) => NarrateLocationId.Fill(__instance, "Awake 时");
}

[HarmonyPatch(typeof(GameWorld), nameof(GameWorld.OnGameStarted))]
public static class NarrateLocationIdOnStart
{
    [HarmonyPriority(Priority.First)]
    static void Prefix(GameWorld __instance) => NarrateLocationId.Fill(__instance, "OnGameStarted 前");
}
