using System.Collections.Generic;
using SemanticVersioning;
using SPTarkov.Server.Core.Models.Spt.Mod;

namespace VisitAPI.Server;

// ── Quest status integer constants ────────────────────────────────────────────

internal static class QuestStatusValue
{
	public const int Locked             = 0;
	public const int AvailableForStart  = 1;
	public const int Started            = 2;
	public const int AvailableForFinish = 3;
	public const int Success            = 4;
	public const int Fail               = 5;
}

// ── HTTP request payload ──────────────────────────────────────────────────────

// Native=true：客户端走原生 FinishQuest 完成流程，服务端只记录完成状态与联动，
// 档案状态写入与奖励发放交给 SPT 原生流程（避免双倍奖励）
public record QuestRequest(string ProfileId, string QuestId, bool Native = false);

// ── SPT mod registration metadata ────────────────────────────────────────────

public record ModMetadata : AbstractModMetadata
{
	public override string ModGuid { get; init; } = "com.visitapi.server";
	public override string Name { get; init; } = "VisitAPI-Server";
	public override string Author { get; init; } = "VisitAPI";
	public override List<string> Contributors { get; init; } = new List<string>();
	public override SemanticVersioning.Version Version { get; init; } = new SemanticVersioning.Version("0.2.1", false);
	public override SemanticVersioning.Range SptVersion { get; init; } = new SemanticVersioning.Range("~4.0.13", false);
	public override List<string> Incompatibilities { get; init; } = new List<string>();
	public override Dictionary<string, SemanticVersioning.Range> ModDependencies { get; init; } = new Dictionary<string, SemanticVersioning.Range>();
	public override string Url { get; init; } = "";
	public override bool? IsBundleMod { get; init; } = false;
	public override string License { get; init; } = "MIT";
}
