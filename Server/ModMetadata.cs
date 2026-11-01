using System.Collections.Generic;
using SemanticVersioning;
using SPTarkov.Server.Core.Models.Spt.Mod;

namespace VisitAPI.Server;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.sora.visitapi.server";
    public string Name { get; init; } = "VisitAPI-Server";
    public string Author { get; init; } = "TricolourSky";
    public List<string> Contributors { get; init; }
    public Version Version { get; init; } = new("1.3.7");
    // 09-24 SORA：写死 ~4.1.6 会把 4.1.0～4.1.5 的用户挡在门外（SPT 按 SemVer 区间拒载），本插件只用 4.1.x 通用 API，放宽到整个 4.1.x
    // 10-06：光放宽这里不够——DLL 照本机 4.1.6 编的话，引用版本钉在 4.1.6.0，旧服务端在这一步之前就加载失败了；现在照 SPT 4.1.0 的 NuGet 包编（见 csproj）
    public Range SptVersion { get; init; } = new("~4.1.0");
    public bool HasPrepatcher { get; init; }
    public List<string> Incompatibilities { get; init; }
    public Dictionary<string, Range> ModDependencies { get; init; }
    public string Url { get; init; }
    public string License { get; init; } = "MIT";
}
