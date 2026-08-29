using System.Collections.Generic;
using System.IO;
using System.Linq;
using VisitAPI.Dialog;

namespace VisitAPI.Native;

public static class DialogFiles
{
    public static readonly DialogLoader Loader = new(Path.Combine(BepInEx.Paths.ConfigPath, "VisitAPI"));

    /// 所有能解析的 .dlg（目录列表按目录 mtime 缓存、解析按文件 mtime 缓存，随便调）
    public static IEnumerable<DialogTree> All() => Loader.TraderIds().Select(Loader.Load).Where(t => t != null);
}
