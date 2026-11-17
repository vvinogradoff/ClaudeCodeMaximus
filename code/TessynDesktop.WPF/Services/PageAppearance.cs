using System.Text.Json;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Services;

/// <summary>What the transcript page needs of the theme and fonts: the theme name and the font sizes as CSS pixel values (the page sets them as CSS variables).</summary>
/// <param name="Theme"><c>light</c> or <c>dark</c>.</param>
/// <param name="AssistantPx">Plain assistant text (system and compaction rows, the live row).</param>
/// <param name="MarkdownPx">Rendered assistant entries.</param>
/// <param name="UserPx">User entries.</param>
/// <param name="Colours">The colours the user changed in this theme, as CSS variable to hex (the page sets them over its own and drops the ones no longer listed).</param>
/// <remarks>Created by Claude</remarks>
public sealed record PageAppearance(string Theme, double AssistantPx, double MarkdownPx, double UserPx, IReadOnlyDictionary<string, string>? Colours = null)
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	public static PageAppearance From(bool dark, AppearanceSettings fonts, IReadOnlyDictionary<string, string>? colours = null) =>
		new(dark ? "dark" : "light", fonts.AssistantFontSize, fonts.AssistantMarkdownFontSize, fonts.UserFontSize, colours);

	/// <summary>The message the page understands (<c>cmd: appearance</c>).</summary>
	public string ToMessage() => Colours is { Count: > 0 }
		? JsonSerializer.Serialize(new { cmd = "appearance", theme = Theme, asst = AssistantPx, md = MarkdownPx, user = UserPx, colours = Colours }, Json)
		: JsonSerializer.Serialize(new { cmd = "appearance", theme = Theme, asst = AssistantPx, md = MarkdownPx, user = UserPx }, Json);   // no colours: the page drops the ones it had
}
