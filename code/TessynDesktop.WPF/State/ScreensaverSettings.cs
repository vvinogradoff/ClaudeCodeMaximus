namespace TessynDesktop.WPF.State;

/// <summary>Screensaver options (settings section <c>screensaver</c>); a timeout of 0 disables it, as in the Avalonia client.</summary>
/// <remarks>Created by Claude</remarks>
[ClientSettingsSection(Constants.SettingsSections.Screensaver)]
public sealed class ScreensaverSettings
{
	public string Directory { get; set; } = string.Empty;

	public int TimeoutSeconds { get; set; } = Constants.Defaults.ScreensaverTimeoutSeconds;

	public int SlideshowIntervalSeconds { get; set; } = Constants.Defaults.ScreensaverSlideshowIntervalSeconds;
}
