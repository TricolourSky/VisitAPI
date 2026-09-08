using System;
using Comfort.Common;
using EFT;

namespace VisitAPI.Native;

/// <summary>「现在是不是在商人访问里」的唯一判据。旧版在 7 个文件里各写了一遍。
/// 2026-09-05：不能只看世界类型——Fika 给 `GameWorld.Create&lt;ClientLocalGameWorld&gt;` 打的补丁因为引用类型泛型共享代码，
/// 连 `Create&lt;NarrateGameWorld&gt;` 一起接管，访问期的世界变成它自己的 hideout 世界，`is NarrateGameWorld` 恒假，
/// 于是访问期相机替换整段失效、引擎拿 1.1 相机预制体建相机当场 NRE（D:\EFT 首测）。改按引擎自己的访问控制器判：
/// `NarrateController._game != null`（从 `NarrateGame.Create` 起到 `End()` 止），世界类型只作兜底。</summary>
public static class Narrating
{
    public static bool Now =>
        (TarkovApplication.Exist(out var app) && app.NarrateControllerAccess != null && app.NarrateControllerAccess.GameExist)
        || (Singleton<GameWorld>.Instantiated && Singleton<GameWorld>.Instance is NarrateGameWorld);
}

/// <summary>「现在在战局里吗」「这个世界是藏身处吗」的唯一判据（体检第二轮：原散在 5 处各写一遍）。</summary>
public static class Raid
{
    public static bool Now => Singleton<AbstractGame>.Instantiated && Singleton<AbstractGame>.Instance.InRaid;

    /// 藏身处 3D 的 LocationId 含 "hideout"（QuestOps 选控制器 / TriggerHost 选触发点种类都靠它）
    public static bool IsHideout(string locationId) => (locationId ?? "").IndexOf("hideout", StringComparison.OrdinalIgnoreCase) >= 0;
}
