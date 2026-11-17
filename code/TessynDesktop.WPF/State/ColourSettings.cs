namespace TessynDesktop.WPF.State;

/// <summary>
/// The colours the user changed (settings section <c>colours</c>): colour key to <c>#RRGGBB</c> / <c>#AARRGGBB</c>. A key that is absent has its default from the
/// <see cref="ColourCatalog"/>, which follows the Avalonia client.
/// </summary>
/// <remarks>Created by Claude</remarks>
[ClientSettingsSection(Constants.SettingsSections.Colours)]
public sealed class ColourSettings
{
	public Dictionary<string, string> Overrides { get; set; } = new(StringComparer.Ordinal);
}
