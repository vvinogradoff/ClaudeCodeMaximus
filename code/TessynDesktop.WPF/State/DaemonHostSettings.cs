namespace TessynDesktop.WPF.State;

/// <summary>
/// Whether this client may start the Avalonia background daemon host when no daemon runs (settings section <c>daemonHost</c>). On by default (WPF is the only client); a stored
/// file without the key gets the default. Off: the client waits ("Waiting for Tessyn to start...") and connects as soon as the Avalonia app (or any host) comes up.
/// <see cref="StartDaemonElevated"/> (FR.32.10, off by default) makes this client start the host through the Windows administrator prompt.
/// </summary>
/// <remarks>Created by Claude</remarks>
[ClientSettingsSection(Constants.SettingsSections.DaemonHost)]
public sealed class DaemonHostSettings
{
	public bool StartBackgroundDaemon { get; set; } = Constants.Defaults.StartBackgroundDaemon;

	/// <summary>When this client starts the host, it starts it elevated (FR.32.10). It never restarts a running daemon because of this setting.</summary>
	public bool StartDaemonElevated { get; set; } = Constants.Defaults.StartDaemonElevated;
}
