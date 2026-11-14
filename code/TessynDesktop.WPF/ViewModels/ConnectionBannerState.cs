namespace TessynDesktop.WPF.ViewModels;

/// <summary>What the connection banner shows.</summary>
/// <remarks>Created by Claude</remarks>
public enum ConnectionBannerState
{
	/// <summary>No running daemon was found (no <c>daemon.json</c>, stale pid, or the daemon refused the connection).</summary>
	DaemonNotFound,
	Connecting,
	Connected,
	Reconnecting,
	Incompatible,
	AuthFailed,
}
