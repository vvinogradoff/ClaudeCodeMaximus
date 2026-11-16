using System.Windows;
using TessynDesktop.WPF.Infrastructure;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// Puts the theme into the running application: Fluent's own light/dark (<see cref="Application.ThemeMode"/>), the app's colour dictionary
/// (<c>Theme.Light.xaml</c> / <c>Theme.Dark.xaml</c>) and the prompt box font size, now and on every change.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AppearanceApplier
{
	private readonly IAppearanceService _appearance;
	private readonly IUiDispatcher _ui;

	public AppearanceApplier(IAppearanceService appearance, IUiDispatcher ui)
	{
		_appearance = appearance;
		_ui = ui;
	}

	/// <summary>Applies the stored look and keeps it current; call once on the UI thread before the window is shown.</summary>
	public void Start()
	{
		Apply(Application.Current.Resources);
		_appearance.Changed += (_, _) => _ui.Post(() => Apply(Application.Current.Resources));
	}

	/// <summary>Swaps the colour dictionary in <paramref name="resources"/> and sets the font resource; the application's ThemeMode follows when there is an application.</summary>
	public void Apply(ResourceDictionary resources)
	{
		var dark = _appearance.IsDark;
		var uri = new Uri(dark ? Constants.Appearance.DarkThemeUri : Constants.Appearance.LightThemeUri, UriKind.Absolute);
		for (var i = resources.MergedDictionaries.Count - 1; i >= 0; i--)
		{
			if (resources.MergedDictionaries[i].Source?.OriginalString.Contains(Constants.Appearance.ThemeDictionaryMarker, StringComparison.OrdinalIgnoreCase) == true)
				resources.MergedDictionaries.RemoveAt(i);
		}

		resources.MergedDictionaries.Add(new ResourceDictionary { Source = uri });
		resources[Constants.Appearance.InputFontSizeKey] = _appearance.Current.InputFontSize;

		if (Application.Current is { } app && ReferenceEquals(app.Resources, resources))
		{
			// Fluent follows Windows by itself only for System; an explicit choice pins it.
#pragma warning disable WPF0001 // ThemeMode is the supported way to select the Fluent variant
			app.ThemeMode = _appearance.Chosen switch
			{
				State.ClientTheme.Dark => ThemeMode.Dark,
				State.ClientTheme.Light => ThemeMode.Light,
				_ => ThemeMode.System,
			};
#pragma warning restore WPF0001
		}
	}
}
