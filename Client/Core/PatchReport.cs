using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;

namespace VisitAPI.Native;

/// <summary>09-26 模组兼容排查用：列出进程里所有被 Harmony 打过补丁的方法，每条写明是哪个模组（程序集）、哪个补丁类、前缀 / 后缀 / 改写 / 收尾。
/// SORA 定的目标是访问建的 GameWorld 和相机不和别的模组冲突——先要知道谁挂在世界、相机、玩家这些生命周期上。
/// 控制台 visit_patchreport，写到 BepInEx\VisitAPI-uidump\patches016-*.txt；只读</summary>
public static class PatchReport
{
    static string Describe(Patch p)
    {
        var m = p.PatchMethod;
        var t = m?.DeclaringType;
        return $"{t?.Assembly.GetName().Name}:{t?.FullName}.{m?.Name}";
    }

    public static string Write()
    {
        try
        {
            var lines = Harmony.GetAllPatchedMethods()
                .Where(m => m != null)
                .Select(m =>
                {
                    var info = Harmony.GetPatchInfo(m);
                    var sb = new StringBuilder();
                    var ps = m.GetParameters().Select(x => x.ParameterType.Name);
                    sb.Append($"{m.DeclaringType?.FullName}.{m.Name}({string.Join(", ", ps)})");
                    if (info != null)
                    {
                        void Add(string kind, System.Collections.ObjectModel.ReadOnlyCollection<Patch> list)
                        {
                            if (list == null) return;
                            foreach (var p in list) sb.Append($"\n    {kind,-10} {Describe(p)}  [owner {p.owner}]");
                        }
                        Add("prefix", info.Prefixes);
                        Add("postfix", info.Postfixes);
                        Add("transpiler", info.Transpilers);
                        Add("finalizer", info.Finalizers);
                    }
                    return sb.ToString();
                })
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();
            var text = new StringBuilder();
            text.AppendLine($"# 0.16 Harmony patch report {DateTime.Now:yyyy-MM-dd HH:mm:ss}  patched methods: {lines.Count}  visiting: {Narrating.Now}");
            foreach (var l in lines) text.AppendLine(l);
            Directory.CreateDirectory(UiDump.Dir);
            var file = Path.Combine(UiDump.Dir, $"patches016-{DateTime.Now:MMdd-HHmmss}.txt");
            File.WriteAllText(file, text.ToString(), new UTF8Encoding(false));
            return file;
        }
        catch (Exception e) { Plugin.Log.LogWarning("[patchreport] failed: " + e.Message); return null; }
    }
}
