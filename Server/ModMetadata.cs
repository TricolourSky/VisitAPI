using System.Collections.Generic;
using SPTarkov.Server.Core.Models.Spt.Mod;
using Version = SemanticVersioning.Version;
using Range = SemanticVersioning.Range;

namespace VisitApiServer;

// SPT 4.0 mod metadata (replaces the old package.json). All members of AbstractModMetadata are abstract,
// so every one must be overridden.
public record VisitApiModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.sora.visitapi.server";
    public override string Name { get; init; } = "VisitAPI-Server";
    public override string Author { get; init; } = "SORA";
    public override List<string>? Contributors { get; init; } = null;
    public override Version Version { get; init; } = new Version("0.4.0");
    public override Range SptVersion { get; init; } = new Range("~4.0.0");
    public override List<string>? Incompatibilities { get; init; } = null;
    public override Dictionary<string, Range>? ModDependencies { get; init; } = null;
    public override string? Url { get; init; } = null;
    public override bool? IsBundleMod { get; init; } = false;
    public override string License { get; init; } = "MIT";
}
