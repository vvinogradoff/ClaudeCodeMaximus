using System.Text.Json;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Services;

/// <summary>What the transcript page needs of the theme and fonts: the theme name and the font sizes as CSS pixel values (the page sets them as CSS variables).</summary>
/// <param name="Theme"><c>light</c> or <c>dark</c>.</param>
/// <param name="AssistantPx">Plain assistant text (system and compaction rows, the live row).</param>
/// <param name="MarkdownPx">Rendered assistant entries.</param>
/// <param name="UserPx">User entries.</param>
/// <param name="Colours">The colours the user changed in this theme, as CSS variable to hex (the page sets them over its own and drops the ones no longer listed).</param>
/// <param name="LineSpacing">Line height as a multiple of the font size (FR.9.12); the page sets it as <c>--lh</c>.</param>
/// <remarks>Created by Claude</remarks>
public sealed record PageAppearance(string Theme, double AssistantPx, double MarkdownPx, double UserPx, IReadOnlyDictionary<string, string>? Colours = null, double LineSpacing = Constants.Defaults.LineSpacing)
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

	public static PageAppearance From(bool dark, AppearanceSettings fonts, IReadOnlyDictionary<string, string>? colours = null) =>
		new(dark ? "dark" : "light", fonts.AssistantFontSize, fonts.AssistantMarkdownFontSize, fonts.UserFontSize, colours is { Count: > 0 } ? colours : null, fonts.LineSpacing);

	/// <summary>The message the page understands (<c>cmd: appearance</c>). No colours: the page drops the ones it had.</summary>
	public string ToMessage() => JsonSerializer.Serialize(new { cmd = "appearance", theme = Theme, asst = AssistantPx, md = MarkdownPx, user = UserPx, colours = Colours, lh = LineSpacing }, Json);
}
