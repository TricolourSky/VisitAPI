using System;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Reflection.Patching;

namespace VisitAPI.Server;

static class Patches
{
    /// <summary>挂上一个补丁；挂不上只打 Error，不往外抛。
    /// name 是日志里的叫法；consequence 是挂不上的后果，写了就接在两种报错后面。</summary>
    public static void Arm<T>(AbstractPatch patch, ISptLogger<T> log, string name, string consequence = null)
    {
        try
        {
            patch.Enable();
            if (!patch.IsActive) log.Error($"[VisitAPI] {name} did NOT arm (IsActive=false after Enable)" + (consequence == null ? "" : "; " + consequence));
        }
        catch (Exception e) { log.Error($"[VisitAPI] {name} failed to arm" + (consequence == null ? "" : $" ({consequence})") + ": " + e); }
    }
}
