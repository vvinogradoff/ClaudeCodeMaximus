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

	/// <summary>No daemon runs and this client may not start one (the setting is off): it waits for Tessyn (the Avalonia app) and connects when it comes up.</summary>
	WaitingForTessyn,

	Connecting,
	Connected,
	Reconnecting,
	Incompatible,
	AuthFailed,
}
