using System.Collections.Generic;

namespace TessynDesktop.Models;

/// <summary>What moving one Claude session into another profile's store did (ISSUE.013 R3).</summary>
/// <param name="Found">True when the session's transcript is (now) in the target store.</param>
/// <param name="TranscriptMoved">True when the <c>.jsonl</c> was moved from another store.</param>
/// <param name="FolderFilesMoved">Files moved from the sibling <c>&lt;id&gt;\</c> folder (merged into an existing one).</param>
/// <param name="OldConfigPaths">Absolute paths under the old config dir found in the transcript; reported, not rewritten.</param>
/// <remarks>Created by Claude</remarks>
public sealed record SessionMigrationReport(
	bool Found,
	bool TranscriptMoved,
	int FolderFilesMoved,
	IReadOnlyList<string> OldConfigPaths)
{
	/// <summary>True when anything was moved.</summary>
	public bool Moved => TranscriptMoved || FolderFilesMoved > 0;

	public static SessionMigrationReport NotFound { get; } = new(false, false, 0, []);
}
