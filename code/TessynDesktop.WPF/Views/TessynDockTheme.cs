using System.Windows;
using AvalonDock.Themes;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// The one docking theme of the application (FR.6.21): AvalonDock's VS2013 templates and styles without its brushes. Every brush they look up (<c>ResourceKeys.*</c>) comes from the
/// application resources, where <c>Styles/DockingColours.Light.xaml</c> / <c>DockingColours.Dark.xaml</c> (merged by the two theme dictionaries) supply Tessyn's colours. A day/night
/// switch is therefore the swap of the application's colour dictionary alone: <see cref="AvalonDock.DockingManager.Theme"/> is set once and never changes, nothing is
/// re-templated, no panel content is recreated and a WebView2 in a panel is not reloaded.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TessynDockTheme : DictionaryTheme
{
	public TessynDockTheme() : base(new ResourceDictionary())
	{
		foreach (var path in Constants.DockingChrome.Vs2013TemplateDictionaries)
			ThemeResourceDictionary.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(path, UriKind.Relative) });
	}
}
