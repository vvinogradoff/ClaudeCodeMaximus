using System.Windows;
using TessynDesktop.WPF.Infrastructure;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// Puts the theme into the running application: Fluent's own light/dark (<see cref="Application.ThemeMode"/>), the app's colour dictionary
/// (<c>Theme.Light.xaml</c> / <c>Theme.Dark.xaml</c>) and the prompt box and tag cloud font sizes, now and on every change.
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

	/// <summary>
	/// Puts <paramref name="uri"/> in as the one theme dictionary. When it already is the one in place nothing is touched: a font change (a Ctrl+wheel spin raises one per
	/// notch) must not tear down and re-load the whole colour dictionary, which re-evaluates every DynamicResource of the window.
	/// </summary>
	private static void SwapThemeDictionary(ResourceDictionary resources, Uri uri)
	{
		var found = false;
		for (var i = resources.MergedDictionaries.Count - 1; i >= 0; i--)
		{
			var source = resources.MergedDictionaries[i].Source;
			if (source?.OriginalString.Contains(Constants.Appearance.ThemeDictionaryMarker, StringComparison.OrdinalIgnoreCase) != true)
				continue;

			if (!found && source == uri)
			{
				found = true;
				continue;
			}

			resources.MergedDictionaries.RemoveAt(i);
		}

		if (!found)
			resources.MergedDictionaries.Add(new ResourceDictionary { Source = uri });
	}

	/// <summary>Swaps the colour dictionary in <paramref name="resources"/> and sets the font resource; the application's ThemeMode follows when there is an application.</summary>
	public void Apply(ResourceDictionary resources)
	{
		var dark = _appearance.IsDark;
		var uri = new Uri(dark ? Constants.Appearance.DarkThemeUri : Constants.Appearance.LightThemeUri, UriKind.Absolute);
		SwapThemeDictionary(resources, uri);
		var current = _appearance.Current;
		resources[Constants.Appearance.InputFontSizeKey] = current.InputFontSize;
		resources[Constants.Appearance.TagFontSizeKey] = current.TagFontSize;
		resources[Constants.Appearance.LineSpacingKey] = current.LineSpacing;

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
