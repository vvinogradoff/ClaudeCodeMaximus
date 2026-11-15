using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>
/// Values of the read-only <c>avalonia-ui</c> settings section: what the Avalonia client keeps for itself, for the one-time import into another client
/// (decision A23 Q8). Nothing here is a secret. The key bindings are a list sorted by action name so the result is stable.
/// </summary>
/// <param name="Theme">"Dark" or "Light".</param>
/// <param name="AssistantFontSize">Font size of assistant text.</param>
/// <param name="AssistantMarkdownFontSize">Font size of rendered Markdown.</param>
/// <param name="UserFontSize">Font size of the user's text.</param>
/// <param name="InputFontSize">Font size of the composer.</param>
/// <param name="LightColors">Colours of the light theme.</param>
/// <param name="DarkColors">Colours of the dark theme.</param>
/// <param name="KeyBindings">Action name and key combination (for example <c>Ctrl+Enter</c>, several separated by a comma).</param>
/// <param name="Screensaver">The screensaver slideshow settings.</param>
/// <remarks>Created by Claude</remarks>
public sealed record AvaloniaUiSettingsDto(
	string Theme,
	double AssistantFontSize,
	double AssistantMarkdownFontSize,
	double UserFontSize,
	double InputFontSize,
	ThemeColorsDto LightColors,
	ThemeColorsDto DarkColors,
	IReadOnlyList<KeyBindingDto> KeyBindings,
	ScreensaverSettingsDto Screensaver);

/// <summary>One theme's colours as <c>#RRGGBB</c> strings.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record ThemeColorsDto(
	string InputBoxBackground,
	string InputBoxText,
	string UserBubbleBackground,
	string UserBubbleText,
	string CodeBlockBackground,
	string CodeBlockText,
	string InlineCodeBackground,
	string InlineCodeText,
	string SystemBubbleBackground,
	string Recency15MinBackground,
	string Recency30MinBackground,
	string Recency60MinBackground);

/// <summary>One key binding of the Avalonia client.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record KeyBindingDto(string Action, string Binding);

/// <summary>The screensaver settings: a folder of images, the idle time before it starts (0 = off) and the seconds per image.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record ScreensaverSettingsDto(string Directory, int TimeoutSeconds, int SlideshowIntervalSeconds);
