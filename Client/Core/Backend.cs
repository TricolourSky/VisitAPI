using Comfort.Common;
using EFT;

namespace VisitAPI.Native;

/// <summary>后端会话 / 档案的取法。
/// 没登录、应用还没建好时一律给 null，调用方自己判。名字带 Visit 前缀：游戏自己的全局命名空间里有个 Backend 类，ChapterUI 那边的文件会先找到它。</summary>
public static class VisitBackend
{
    public static IEftSession Session
    {
        get
        {
            try { return Singleton<ClientApplication<IEftSession>>.Instance?.GetClientBackEndSession(); }
            catch { return null; }
        }
    }

    public static Profile Profile => Session?.Profile;

    public static ProfileVariablesStorage Variables => Profile?.ProfileVariables;
}
