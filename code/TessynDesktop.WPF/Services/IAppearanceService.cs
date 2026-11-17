using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// The theme (Light, Dark or follow Windows) and the four font sizes as the window shows them. The values live in the <c>appearance</c> settings section;
/// this service turns them into the effective theme, takes the day/night toggle and the Ctrl+wheel font changes, and tells the views to restyle.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IAppearanceService
{
	/// <summary>What the user chose; System follows Windows.</summary>
	ClientTheme Chosen { get; }

	/// <summary>True when the effective theme is Dark (the choice, or Windows' own theme for System).</summary>
	bool IsDark { get; }

	/// <summary>The current font sizes and theme choice (a copy).</summary>
	AppearanceSettings Current { get; }

	/// <summary>The colours the user changed in the effective theme that the transcript page takes as CSS variables (<c>--bg</c> to <c>#RRGGBB</c>); empty when none.</summary>
	IReadOnlyDictionary<string, string> PageColours { get; }

	/// <summary>The effective theme, a font size or a page colour changed. Raised on the thread that caused it; views marshal to the UI thread.</summary>
	event EventHandler? Changed;

	/// <summary>The day/night button: switches to the opposite of the effective theme and remembers it as the explicit choice.</summary>
	void ToggleTheme();

	/// <summary>Ctrl+wheel: changes one font size by <paramref name="delta"/> points, within <see cref="Constants.Appearance.ZoomMin"/> and <see cref="Constants.Appearance.ZoomMax"/>.</summary>
	void AdjustFont(FontTarget target, int delta);
}
