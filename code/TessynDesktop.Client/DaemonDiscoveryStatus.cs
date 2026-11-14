namespace TessynDesktop.Client;

/// <summary>Outcome of <see cref="DaemonDiscovery.DiscoverAsync"/>.</summary>
/// <remarks>Created by Claude</remarks>
public enum DaemonDiscoveryStatus
{
	/// <summary>A running daemon was found and its token was read.</summary>
	Found,

	/// <summary><c>daemon.json</c> does not exist: no daemon has been started (or it exited cleanly).</summary>
	NoDaemonFile,

	/// <summary><c>daemon.json</c> exists but cannot be used (not JSON, or fields missing).</summary>
	InvalidFile,

	/// <summary>The file names a process that no longer runs: the daemon crashed and left the file behind.</summary>
	StaleFile,

	/// <summary>The token file is missing or cannot be decrypted (another Windows user, corrupt, or not Windows).</summary>
	TokenUnavailable,
}
