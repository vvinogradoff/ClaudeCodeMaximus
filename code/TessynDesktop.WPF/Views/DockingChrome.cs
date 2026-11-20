using System.Windows;
using System.Windows.Controls;
using AvalonDock;
using AvalonDock.Controls;
using AvalonDock.Themes;
using Microsoft.Extensions.Logging;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// The compact docking chrome of a <see cref="DockingManager"/> (FR.6.13, FR.6.20): hands the styles of <c>Styles/DockingChrome.xaml</c> to the manager as local values (they
/// survive a theme switch; the theme's own style would bring the caption bars back), and holds the keyboard router for floating panels, the focus guard and the slide-out closer.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DockingChrome
{
	private readonly DockingKeyboardRouter _router = new();

	public DockingChrome(DockingManager manager, FrameworkElement resourceHost)
	{
		Focus = new DockingFocusGuard(manager);
		SlideOut = new AutoHideWindowCloser(manager);
		if (resourceHost.TryFindResource(Constants.DockingChrome.PaneStyleKey) is Style pane)
			manager.AnchorablePaneControlStyle = pane;

		if (DocumentPaneStyle(manager, resourceHost) is { } document)
			manager.DocumentPaneControlStyle = document;

		manager.AnchorableContextMenu = Menu(resourceHost, Constants.DockingChrome.AnchorableMenuKey);
		manager.DocumentContextMenu = Menu(resourceHost, Constants.DockingChrome.DocumentMenuKey);
	}

	public DockingFocusGuard Focus { get; }

	public AutoHideWindowCloser SlideOut { get; }

	/// <summary>Gives the parts a log (the slide-out closer logs whether the library's method was found).</summary>
	public void UseLogger(ILogger logger)
	{
		SlideOut.UseLogger(logger);
		Focus.UseLogger(logger);
	}

	/// <summary>The command registry the floating panels' keys go to (FR.6.20).</summary>
	public void UseShellCommands(IShellCommands commands) => _router.UseCommands(commands);

	/// <summary>Called for every floating window the library creates.</summary>
	public void OnFloatingWindowCreated(LayoutFloatingWindowControl window) => _router.Attach(window);

	/// <summary>
	/// The document pane's style: the library's own (from the manager's theme), with the flat tab item style of <c>DockingChrome.xaml</c> (the accent line under the selected tab,
	/// FR.6.21). Null when the theme has no such style (the library's own tabs stay then).
	/// </summary>
	private static Style? DocumentPaneStyle(DockingManager manager, FrameworkElement resourceHost)
	{
		if (manager.Theme is not DictionaryTheme { ThemeResourceDictionary: { } library } ||
		    library[Constants.DockingChrome.LibraryDocumentPaneStyleKey] is not Style baseStyle ||
		    resourceHost.TryFindResource(Constants.DockingChrome.DocumentTabStyleKey) is not Style tab)
			return null;

		var style = new Style(typeof(LayoutDocumentPaneControl), baseStyle);
		style.Setters.Add(new Setter(ItemsControl.ItemContainerStyleProperty, tab));
		return style;
	}

	private static ContextMenu? Menu(FrameworkElement resourceHost, string key)
	{
		if (resourceHost.TryFindResource(key) is not ContextMenu menu)
			return null;

		// A menu whose items are all hidden (the Session view allows none) must not open as an empty popup.
		menu.Opened += (_, _) =>
		{
			if (!menu.Items.OfType<UIElement>().Any(item => item.Visibility == Visibility.Visible))
				menu.IsOpen = false;
		};
		return menu;
	}
}
