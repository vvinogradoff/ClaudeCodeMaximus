namespace TessynDesktop.WPF.State;

/// <summary>One editable colour: its key, label, group in the Colours tab and its default in the light and the dark theme (hex).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record ColourDefinition(string Key, string Label, string Group, string LightDefault, string DarkDefault)
{
	/// <summary>The application resource that carries the brush of this colour.</summary>
	public string ResourceKey => Constants.Colours.ResourcePrefix + Key;
}
