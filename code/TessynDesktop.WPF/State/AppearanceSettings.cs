using System.Text.Json.Serialization;

namespace TessynDesktop.WPF.State;

/// <summary>Theme and font sizes (settings section <c>appearance</c>). Stored only; applying them is the theme package's job.</summary>
/// <remarks>Created by Claude</remarks>
[ClientSettingsSection(Constants.SettingsSections.Appearance)]
public sealed class AppearanceSettings
{
	[JsonConverter(typeof(JsonStringEnumConverter))]
	public ClientTheme Theme { get; set; } = ClientTheme.System;

	/// <summary>How the session rows show the date of the last prompt.</summary>
	[JsonConverter(typeof(JsonStringEnumConverter))]
	public SessionDateFormat DateFormat { get; set; } = SessionDateFormat.Absolute;

	public double AssistantFontSize { get; set; } = Constants.Defaults.FontSize;

	public double AssistantMarkdownFontSize { get; set; } = Constants.Defaults.FontSize;

	public double UserFontSize { get; set; } = Constants.Defaults.FontSize;

	public double InputFontSize { get; set; } = Constants.Defaults.FontSize;

	/// <summary>Line height as a multiple of the font size (FR.9.12), for the session output and the Focus view.</summary>
	public double LineSpacing { get; set; } = Constants.Defaults.LineSpacing;

	/// <summary>The tag chips and the "+N" hint of the session tag area (FR.24).</summary>
	public double TagFontSize { get; set; } = Constants.Defaults.TagFontSize;
}
