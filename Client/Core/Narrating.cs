using System;
using Comfort.Common;
using EFT;

namespace VisitAPI.Native;

public static class Narrating
{
    public const string WorldObjectName = "NarrateWorld";

    /// 1.3.4（清点第 32 项）：「是不是访问」认原生给访问游戏的类型 EGameType.Narrate。访问游戏不注册成全局 Singleton&lt;AbstractGame&gt;，
    /// 所以从访问控制器上取；世界那一支留给世界刚建、控制器还没记下它的那几帧（GameWorld.Awake 时）
    public static bool Now =>
        (TarkovApplication.Exist(out var app) && app.NarrateControllerAccess != null && IsVisitGame(app.NarrateControllerAccess._game))
        || (Singleton<GameWorld>.Instantiated && IsVisitWorld(Singleton<GameWorld>.Instance));

    public static bool IsVisitGame(AbstractGame game) => game != null && game.GameType == EGameType.Narrate;

    static GameWorld _nameChecked;
    static bool _nameIsVisit;

    public static bool IsVisitWorld(GameWorld world)
    {
        if (world == null) return false;
        if (world is NarrateGameWorld) return true;
        if (TarkovApplication.Exist(out var app) && app.NarrateControllerAccess != null && ReferenceEquals(app.NarrateControllerAccess._gameWorld, world)) return true;
        // 09-24 审查低项：战局里每帧都会问到这里（ReflectionPin、PrismTransplant 等逐帧补丁），world.name 每次都新分配字符串。
        // 世界的名字建好就不变，按世界实例缓存
        if (!ReferenceEquals(world, _nameChecked))
        {
            _nameChecked = world;
            try { _nameIsVisit = world.name == WorldObjectName; }
            catch { _nameIsVisit = false; }
        }
        return _nameIsVisit;
    }
}

public static class Raid
{
    public static bool Now => Singleton<AbstractGame>.Instantiated && Singleton<AbstractGame>.Instance.InRaid;

    public static bool IsHideout(string locationId) => (locationId ?? "").IndexOf("hideout", StringComparison.OrdinalIgnoreCase) >= 0;
}
