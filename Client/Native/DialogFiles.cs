using System.IO;
using VisitAPI.Dialog;

namespace VisitAPI.Native;

public static class DialogFiles
{
    public static readonly DialogLoader Loader = new(Path.Combine(BepInEx.Paths.ConfigPath, "VisitAPI"));
}
