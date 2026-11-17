using System.Text.Json;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Services;

/// <summary>What the transcript page needs of the theme and fonts: the theme name and the font sizes as CSS pixel values (the page sets them as CSS variables).</summary>
/// <param name="Theme"><c>light</c> or <c>dark</c>.</param>
/// <param name="AssistantPx">Plain assistant text (system and compaction rows, the live row).</param>
/// <param name="MarkdownPx">Rendered assistant entries.</param>
/// <param name="UserPx">User entries.</param>
/// <param name="SystemRow">Background of the system rows (tool calls) as <c>#RRGGBB</c>/<c>#AARRGGBB</c>; null leaves the page's own.</param>
/// <remarks>Created by Claude</remarks>
public sealed record PageAppearance(string Theme, double AssistantPx, double MarkdownPx, double UserPx, string? SystemRow = null)
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

	public static PageAppearance From(bool dark, AppearanceSettings fonts, string? systemRow = null) =>
		new(dark ? "dark" : "light", fonts.AssistantFontSize, fonts.AssistantMarkdownFontSize, fonts.UserFontSize, systemRow);

	/// <summary>The message the page understands (<c>cmd: appearance</c>).</summary>
	public string ToMessage() => JsonSerializer.Serialize(new { cmd = "appearance", theme = Theme, asst = AssistantPx, md = MarkdownPx, user = UserPx, sys = SystemRow }, Json);
}
