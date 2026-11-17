namespace TessynDesktop.WPF.State;

/// <summary>One editable colour: its key, label, group in the Colours tab and its default in the light and the dark theme (hex).</summary>
/// <param name="Targets">
/// Resources of the application the colour also drives while the user has changed it (the Fluent brushes and their colours, or the app's own theme brushes), by brush name; empty
/// for a colour only the views read as <see cref="ResourceKey"/>. An unchanged colour leaves these alone, so Fluent keeps its own value.
/// </param>
/// <param name="Css">The CSS variable of the transcript page this colour sets (<c>--bg</c>); null for a colour the page does not use.</param>
/// <remarks>Created by Claude</remarks>
public sealed record ColourDefinition(string Key, string Label, string Group, string LightDefault, string DarkDefault, string[]? Targets = null, string? Css = null)
{
	/// <summary>The application resource that carries the brush of this colour.</summary>
	public string ResourceKey => Constants.Colours.ResourcePrefix + Key;

	public IReadOnlyList<string> TargetBrushes => Targets ?? [];
}
