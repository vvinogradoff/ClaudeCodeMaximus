namespace TessynDesktop.WPF.State;

/// <summary>
/// Whether this client may start the Avalonia background daemon host when no daemon runs (settings section <c>daemonHost</c>). Off by default: the client then waits
/// ("Waiting for Tessyn to start...") and connects as soon as the Avalonia app (or any host) comes up, so a visible Avalonia window can start after this client.
/// </summary>
/// <remarks>Created by Claude</remarks>
[ClientSettingsSection(Constants.SettingsSections.DaemonHost)]
public sealed class DaemonHostSettings
{
	public bool StartBackgroundDaemon { get; set; } = Constants.Defaults.StartBackgroundDaemon;
}
