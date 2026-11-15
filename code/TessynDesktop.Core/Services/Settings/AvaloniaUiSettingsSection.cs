using System;
using System.Linq;
using System.Text.Json.Nodes;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// The read-only <c>avalonia-ui</c> section (decision A23 Q8): the Avalonia client's theme, colours, font sizes, key bindings and screensaver, served so
/// that another client can import them once. It reads existing members of <see cref="AppSettingsModel"/> and writes none: <c>settings.update</c> refuses
/// it, and it takes no part in change detection (see <see cref="IsReadOnly"/>).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AvaloniaUiSettingsSection : ISettingsSection
{
	/// <inheritdoc />
	public string Name => RemoteProtocol.SettingsApi.AvaloniaUiSection;

	/// <inheritdoc />
	public bool IsReadOnly => true;

	/// <inheritdoc />
	public object Read(AppSettingsModel settings) =>
		new AvaloniaUiSettingsDto(
			settings.Theme,
			settings.AssistantFontSize,
			settings.AssistantMarkdownFontSize,
			settings.UserFontSize,
			settings.InputFontSize,
			ToDto(settings.LightColors),
			ToDto(settings.DarkColors),
			[.. (settings.KeyBindings?.Bindings ?? []).OrderBy(b => b.Key, StringComparer.Ordinal).Select(b => new KeyBindingDto(b.Key, b.Value))],
			new ScreensaverSettingsDto(settings.ScreensaverDirectory, settings.ScreensaverTimeout, settings.ScreensaverSlideshowInterval));

	/// <inheritdoc />
	public Action CaptureRestore(AppSettingsModel settings) => static () => { };

	/// <inheritdoc />
	public void Apply(AppSettingsModel settings, JsonNode patch) =>
		throw new TdcpInvalidParamsException($"Settings section '{Name}' is read-only");

	private static ThemeColorsDto ToDto(ThemeColorsModel colors) =>
		new(colors.InputBoxBackground, colors.InputBoxText, colors.UserBubbleBackground, colors.UserBubbleText, colors.CodeBlockBackground, colors.CodeBlockText,
			colors.InlineCodeBackground, colors.InlineCodeText, colors.SystemBubbleBackground, colors.Recency15MinBackground, colors.Recency30MinBackground, colors.Recency60MinBackground);
}
