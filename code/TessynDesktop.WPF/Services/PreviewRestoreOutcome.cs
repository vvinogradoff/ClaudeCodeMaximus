namespace TessynDesktop.WPF.Services;

/// <summary>What became of one saved preview window or panel at the restore (FR.29.22).</summary>
/// <remarks>Created by Claude</remarks>
public enum PreviewRestoreOutcome
{
	/// <summary>Reopened; it is in the saved list through its live window.</summary>
	Reopened,

	/// <summary>Skipped for a permanent reason (refused or missing file, group deleted, mode changed, another session's auto-close group): not reopened by itself, gone from the saved list.</summary>
	Dropped,

	/// <summary>Skipped for a transient reason (daemon or file error that is not a refusal, no docking area): stays in the saved list for the next start.</summary>
	Kept,
}
