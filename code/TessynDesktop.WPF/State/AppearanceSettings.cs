using System.Text.Json.Serialization;

namespace TessynDesktop.WPF.State;

/// <summary>Theme and font sizes (settings section <c>appearance</c>). Stored only; applying them is the theme package's job.</summary>
/// <remarks>Created by Claude</remarks>
[ClientSettingsSection(Constants.SettingsSections.Appearance)]
public sealed class AppearanceSettings
{
	[JsonConverter(typeof(JsonStringEnumConverter))]
	public ClientTheme Theme { get; set; } = ClientTheme.System;

	public double AssistantFontSize { get; set; } = Constants.Defaults.FontSize;

	public double AssistantMarkdownFontSize { get; set; } = Constants.Defaults.FontSize;

	public double UserFontSize { get; set; } = Constants.Defaults.FontSize;

	public double InputFontSize { get; set; } = Constants.Defaults.FontSize;
}
