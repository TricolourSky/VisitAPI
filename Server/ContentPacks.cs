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
/// 五个加载器都从这里拿包列表，谁先到谁触发扫描并打日志（ItemLoader 在 TraderRegistration 阶段最早）。
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
                if (p.IsLegacy) log?.Warning("[VisitAPI] 老布局：db\\ 还在 DLL 旁边，这次照读。请把 db\\ 里的东西和 images\\、bundles\\ 搬进 packs\\<包名>\\（每个包自带 quests / locales / images…），下个版本不再读 db\\");
                else if (p.Error != null) log?.Warning($"[VisitAPI] 包 {p.Name}：{p.Error}，按文件夹名当包读");
                else if (!PackLayout.Satisfies(p.Requires, Version)) log?.Error($"[VisitAPI] 包 {p.Label} 要求 VisitAPI {p.Requires}，本机是 {Version}——照样加载，出了问题先升级");
                else log?.Info($"[VisitAPI] 包 {p.Label}{(p.Author.Length > 0 ? "（" + p.Author + "）" : "")}");
                foreach (var need in p.Needs)
                    if (!list.Any(x => x.Name.Equals(need, StringComparison.OrdinalIgnoreCase))) log?.Warning($"[VisitAPI] 包 {p.Name} 建议一起装 {need}，本机没有");
            }
            if (list.Count == 0) log?.Info("[VisitAPI] 没有内容包（packs\\ 是空的、也没有 db\\）：只加载你自己的 .dlg");
            return _all = list;
        }
    }

    /// <summary>bundles 登记用：包文件夹相对 SPT 工作目录的路径（SPT 按 ModPath/bundles/key 找文件，所以模型文件必须住在包自己的 bundles\ 下）。</summary>
    public static string Rel(PackInfo p) => Path.GetRelativePath(Directory.GetCurrentDirectory(), p.Folder).Replace('\\', '/');
}
