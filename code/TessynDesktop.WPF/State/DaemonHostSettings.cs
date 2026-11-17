namespace TessynDesktop.WPF.State;

/// <summary>
/// Whether this client may start the Avalonia background daemon host when no daemon runs (settings section <c>daemonHost</c>). On by default (WPF is the only client); a stored
/// file without the key gets the default. Off: the client waits ("Waiting for Tessyn to start...") and connects as soon as the Avalonia app (or any host) comes up.
/// </summary>
/// <remarks>Created by Claude</remarks>
[ClientSettingsSection(Constants.SettingsSections.DaemonHost)]
public sealed class DaemonHostSettings
{
	public bool StartBackgroundDaemon { get; set; } = Constants.Defaults.StartBackgroundDaemon;
}
