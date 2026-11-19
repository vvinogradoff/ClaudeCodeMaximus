using System.Windows;
using System.Windows.Controls;
using AvalonDock;
using AvalonDock.Controls;
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

		manager.AnchorableContextMenu = Menu(resourceHost, Constants.DockingChrome.AnchorableMenuKey);
		manager.DocumentContextMenu = Menu(resourceHost, Constants.DockingChrome.DocumentMenuKey);
	}

	public DockingFocusGuard Focus { get; }

	public AutoHideWindowCloser SlideOut { get; }

	/// <summary>The command registry the floating panels' keys go to (FR.6.20).</summary>
	public void UseShellCommands(IShellCommands commands) => _router.UseCommands(commands);

	/// <summary>Called for every floating window the library creates.</summary>
	public void OnFloatingWindowCreated(LayoutFloatingWindowControl window) => _router.Attach(window);

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
