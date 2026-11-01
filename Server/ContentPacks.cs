using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Utils;
using VisitAPI.Packs;

namespace VisitAPI.Server;

/// <summary>
/// 内容包清单（进程内只扫一次，09-23 文件树整理）：packs\&lt;名字&gt;\ 各一个（布局定义在共享的 PackLayout）；老的 db\ 1.3.7 起不再读，只提示搬家。
/// 各加载器 / 路由都从这里拿包列表，谁先到谁触发扫描，包有问题时也在那一次报（最早的是 TraderRegistration 阶段的 ItemLoader / TraderLoader）。
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
            if (Directory.Exists(Path.Combine(ModDir, "db"))) log?.Warning("[VisitAPI] Old layout: db\\ next to the DLL is no longer read. Move the contents of db\\ plus images\\ and bundles\\ into packs\\<pack name>\\ (each pack carries its own quests / locales / images...)");
            foreach (var p in list)
            {
                if (p.Error != null) log?.Warning($"[VisitAPI] Pack {p.Name}: {PackError(p.Error)}, reading it as a pack named after its folder");
                else if (!PackLayout.Satisfies(p.Requires, Version)) log?.Error($"[VisitAPI] Pack {p.Label} requires VisitAPI {p.Requires}, this install is {Version} - loading anyway; upgrade first if problems occur");
                foreach (var need in p.Needs)
                    if (!list.Any(x => x.Name.Equals(need, StringComparison.OrdinalIgnoreCase))) log?.Warning($"[VisitAPI] Pack {p.Name} recommends also installing {need}, which is not installed");
            }
            return _all = list;
        }
    }

    /// PackLayout（编辑器仓库共用的源码）写的报错是中文，编辑器界面照用；服务端日志默认英文，只在这里换成英文
    static string PackError(string e) =>
        e == "没有 pack.json" ? "no pack.json"
        : e.StartsWith("pack.json 读不动：", StringComparison.Ordinal) ? "pack.json could not be read: " + e.Substring("pack.json 读不动：".Length)
        : e;

    /// <summary>bundles 登记用：包文件夹相对 SPT 工作目录的路径（SPT 按 ModPath/bundles/key 找文件，所以模型文件必须住在包自己的 bundles\ 下）。</summary>
    public static string Rel(PackInfo p) => Path.GetRelativePath(Directory.GetCurrentDirectory(), p.Folder).Replace('\\', '/');

    /// <summary>包里 locales\ 的一个文件：Table 为 null 时 Error 是读不动的原因（文件内容就是 null 时两个都是 null）。</summary>
    public sealed record LocaleFile(string File, Dictionary<string, string> Table, string Error);

    static readonly Dictionary<PackInfo, IReadOnlyList<LocaleFile>> LocaleFiles = new();

    /// <summary>包里的语言文件，每个只读一次（TraderLoader 和 QuestLoader 共用）；两边都只读不改。
    /// 读不动的不在这里报：两边各报各的（TraderLoader 在 TraderRegistration、QuestLoader 在 PostLoad）。</summary>
    public static IReadOnlyList<LocaleFile> Locales(PackInfo p, JsonUtil json)
    {
        lock (Gate)
        {
            if (LocaleFiles.TryGetValue(p, out var hit)) return hit;
            var list = new List<LocaleFile>();
            foreach (var file in PackLayout.DataFiles(p, "locales"))
            {
                try { list.Add(new LocaleFile(file, json.DeserializeFromFile<Dictionary<string, string>>(file), null)); }
                catch (Exception e) { list.Add(new LocaleFile(file, null, e.Message)); }
            }
            return LocaleFiles[p] = list;
        }
    }
}
