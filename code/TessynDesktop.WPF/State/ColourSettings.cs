namespace TessynDesktop.WPF.State;

/// <summary>
/// The colours the user changed (settings section <c>colours</c>), kept separately for the Light and the Dark theme: colour key to <c>#RRGGBB</c> / <c>#AARRGGBB</c>. A key that is
/// absent has its default from the <see cref="ColourCatalog"/>. Any key is stored as is, so a colour added to the catalog needs nothing here.
/// </summary>
/// <remarks>
/// <see cref="Overrides"/> is the single-theme store of earlier builds. It is only read, as the fallback of both themes (so an old value shows in Light and Dark alike) until
/// the first change in the Colours tab moves it into <see cref="Light"/> and <see cref="Dark"/> (<see cref="Migrate"/>).
/// Created by Claude
/// </remarks>
[ClientSettingsSection(Constants.SettingsSections.Colours)]
public sealed class ColourSettings
{
	public Dictionary<string, string> Light { get; set; } = new(StringComparer.Ordinal);

	public Dictionary<string, string> Dark { get; set; } = new(StringComparer.Ordinal);

	/// <summary>The earlier build's colours for both themes; emptied by <see cref="Migrate"/>.</summary>
	public Dictionary<string, string> Overrides { get; set; } = new(StringComparer.Ordinal);

	/// <summary>The changes of one theme as the applier sees them: the legacy colours first, the theme's own on top.</summary>
	public IReadOnlyDictionary<string, string> For(bool dark)
	{
		var merged = new Dictionary<string, string>(Overrides ?? [], StringComparer.Ordinal);
		foreach (var (key, value) in (dark ? Dark : Light) ?? [])
			merged[key] = value;

		return merged;
	}

	/// <summary>The stored (editable) dictionary of the theme.</summary>
	public Dictionary<string, string> Of(bool dark)
	{
		if (dark)
			return Dark ??= new(StringComparer.Ordinal);

		return Light ??= new(StringComparer.Ordinal);
	}

	/// <summary>Moves the legacy single-theme colours into both themes (a theme's own value wins) and empties them.</summary>
	public void Migrate()
	{
		if (Overrides is not { Count: > 0 })
			return;

		foreach (var (key, value) in Overrides)
		{
			var hex = ColourCatalog.IsValid(value) ? ColourCatalog.Normalize(value) : value;
			Of(false).TryAdd(key, hex);
			Of(true).TryAdd(key, hex);
		}

		Overrides = new(StringComparer.Ordinal);
	}
}
