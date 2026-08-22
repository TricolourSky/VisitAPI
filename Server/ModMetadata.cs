using System.Collections.Generic;
using SemanticVersioning;
using SPTarkov.Server.Core.Models.Spt.Mod;

namespace VisitAPI.Server;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.sora.visitapi";
    public string Name { get; init; } = "VisitAPI-Server";
    public string Author { get; init; } = "TricolourSky";
    public List<string> Contributors { get; init; }
    public Version Version { get; init; } = new("1.1.0");
    public Range SptVersion { get; init; } = new("~4.1.1");
    public bool HasPrepatcher { get; init; }
    public List<string> Incompatibilities { get; init; }
    public Dictionary<string, Range> ModDependencies { get; init; }
    public string Url { get; init; }
    public string License { get; init; } = "MIT";
}
