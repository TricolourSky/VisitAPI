using System.Collections.Generic;
using System.IO;
using System.Linq;
using VisitAPI.Dialog;

namespace VisitAPI.Native;

public static class DialogFiles
{
    public static readonly DialogLoader Loader = new(Path.Combine(BepInEx.Paths.ConfigPath, "VisitAPI"));

    public static IEnumerable<DialogTree> All() => Loader.TraderIds().Select(Loader.Load).Where(t => t != null);

    public static DialogTree Tree(string traderId) => traderId != null && Loader.TraderIds().Contains(traderId) ? Loader.Load(traderId) : null;
}
