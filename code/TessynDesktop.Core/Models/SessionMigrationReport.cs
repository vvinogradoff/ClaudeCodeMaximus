using System.Collections.Generic;

namespace TessynDesktop.Models;

/// <summary>What moving one Claude session into another profile's store did (ISSUE.013 R3).</summary>
/// <param name="Found">True when the session's transcript is (now) in the target store.</param>
/// <param name="TranscriptMoved">True when the <c>.jsonl</c> was moved from another store.</param>
/// <param name="FolderFilesMoved">Files moved from the sibling <c>&lt;id&gt;\</c> folder (merged into an existing one).</param>
/// <param name="OldConfigPaths">Absolute paths under the old config dir found in the transcript; reported, not rewritten.</param>
/// <param name="Failed">Files (or folders) that could not be moved, e.g. held open by a running CLI; the move of the others went on.</param>
/// <remarks>Created by Claude</remarks>
public sealed record SessionMigrationReport(
	bool Found,
	bool TranscriptMoved,
	int FolderFilesMoved,
	IReadOnlyList<string> OldConfigPaths,
	IReadOnlyList<string>? FailedPaths = null)
{
	/// <summary>Paths that could not be moved; empty when everything moved.</summary>
	public IReadOnlyList<string> Failed => FailedPaths ?? [];

	/// <summary>True when anything was moved.</summary>
	public bool Moved => TranscriptMoved || FolderFilesMoved > 0;

	/// <summary>True when at least one file could not be moved.</summary>
	public bool HasFailures => Failed.Count > 0;

	public static SessionMigrationReport NotFound { get; } = new(false, false, 0, []);
}
