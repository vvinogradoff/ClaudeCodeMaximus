namespace TessynDesktop.WPF.Switch;

/// <summary>Whether the daemon's source differs between the running daemon and the newest build (FR.32.9).</summary>
/// <remarks>Created by Claude</remarks>
public enum DaemonChangeDecision
{
	/// <summary>Nothing in the daemon changed: no banner.</summary>
	Unchanged,

	/// <summary>The daemon's source changed: the banner.</summary>
	Changed,

	/// <summary>It cannot be told (unknown commit, no git, a git failure or timeout): the time rule of FR.32.7 decides.</summary>
	Unknown,
}
