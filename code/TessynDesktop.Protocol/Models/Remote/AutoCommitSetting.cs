namespace TessynDesktop.Models.Remote;

/// <summary>The tri-state Auto-Commit toggle (FR.11.3). On the wire: <c>true</c>, <c>false</c> and JSON <c>null</c>.</summary>
/// <remarks>Created by Claude</remarks>
public enum AutoCommitSetting
{
	/// <summary>Commit the changes (wire <c>true</c>).</summary>
	Commit,

	/// <summary>Do not commit (wire <c>false</c>).</summary>
	DoNotCommit,

	/// <summary>No commit-related instruction at all (wire <c>null</c>).</summary>
	NoPreference,
}
