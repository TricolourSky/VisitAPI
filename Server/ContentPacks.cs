using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SPTarkov.Common.Models.Logging;
using VisitAPI.Packs;

namespace VisitAPI.Server;

/// <summary>
/// 内容包清单（进程内只扫一次，09-23 文件树整理）：packs\&lt;名字&gt;\ 各一个，老的 db\ 当「(db)」包照读并提示搬家（布局定义在共享的 PackLayout）。
/// 五个加载器都从这里拿包列表，谁先到谁触发扫描，包有问题时也在那一次报（ItemLoader 在 TraderRegistration 阶段最早）。
/// </summary>
static class ContentPacks
{
    static readonly object Gate = new();
    static IReadOnlyList<PackInfo> _all;

    public static string ModDir => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
    public static string Version => (Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0)).ToString(3);

    public static IReadOnlyList<PackInfo> All() => All<object>(null);

    public static IReadOnlyList<PackInfo> All<T>(ISptLogger<T> log)
    {
        lock (Gate)
        {
            if (_all != null) return _all;
            var list = PackLayout.Discover(ModDir);
            foreach (var p in list)
            {
                if (p.IsLegacy) log?.Warning("[VisitAPI] Legacy layout: db\\ is still next to the DLL, reading it this time. Please move the contents of db\\ plus images\\ and bundles\\ into packs\\<pack name>\\ (each pack carries its own quests / locales / images...); the next version will no longer read db\\");
                else if (p.Error != null) log?.Warning($"[VisitAPI] Pack {p.Name}: {PackError(p.Error)}, reading it as a pack named after its folder");
                else if (!PackLayout.Satisfies(p.Requires, Version)) log?.Error($"[VisitAPI] Pack {p.Label} requires VisitAPI {p.Requires}, this install is {Version} - loading anyway; upgrade first if problems occur");
                foreach (var need in p.Needs)
                    if (!list.Any(x => x.Name.Equals(need, StringComparison.OrdinalIgnoreCase))) log?.Warning($"[VisitAPI] Pack {p.Name} recommends also installing {need}, which is not installed");
            }
            return _all = list;
        }
    }

    /// <summary>bundles 登记用：包文件夹相对 SPT 工作目录的路径（SPT 按 ModPath/bundles/key 找文件，所以模型文件必须住在包自己的 bundles\ 下）。</summary>
    /// PackLayout（编辑器仓库共用的源码）写的报错是中文，编辑器界面照用；服务端日志默认英文，只在这里换成英文
    static string PackError(string e) =>
        e == "没有 pack.json" ? "no pack.json"
        : e.StartsWith("pack.json 读不动：", StringComparison.Ordinal) ? "pack.json could not be read: " + e.Substring("pack.json 读不动：".Length)
        : e;

    public static string Rel(PackInfo p) => Path.GetRelativePath(Directory.GetCurrentDirectory(), p.Folder).Replace('\\', '/');
}
