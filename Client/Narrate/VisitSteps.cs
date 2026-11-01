using System;

namespace VisitAPI.Native;

/// <summary>访问布置 / 收尾、房间布置里「一步失败不影响其余」的包装。
/// failure 是日志那句话的样子：{0} 填步骤名、{1} 填异常——三处的措辞各不相同，各自传各自的。</summary>
public static class VisitSteps
{
    public static void Run(string failure, string name, Action act)
    {
        try { act(); }
        catch (Exception e) { Plugin.Log.LogError(string.Format(failure, name, e)); }
    }
}
