namespace TessynDesktop.WPF.ViewModels;

/// <summary>What the connection banner shows.</summary>
/// <remarks>Created by Claude</remarks>
public enum ConnectionBannerState
{
	/// <summary>No running daemon was found (no <c>daemon.json</c>, stale pid, or the daemon refused the connection).</summary>
	DaemonNotFound,

	/// <summary><c>daemon.json</c> names a live daemon but the connection was refused or timed out.</summary>
	DaemonUnreachable,

	/// <summary>No daemon runs; the client started the background host and waits for it to publish <c>daemon.json</c>.</summary>
	StartingDaemon,

	Connecting,
	Connected,
	Reconnecting,
	Incompatible,
	AuthFailed,
}
