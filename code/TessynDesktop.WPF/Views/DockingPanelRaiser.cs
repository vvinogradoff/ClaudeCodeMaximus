using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using AvalonDock;
using AvalonDock.Controls;
using AvalonDock.Layout;
using Microsoft.Extensions.Logging;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// Brings a panel forward without taking the keyboard focus (FR.6.16, FR.6.20): slides an auto-hidden panel out (the click on its edge tab, raised in code, as the library has no public
/// call) and lifts a floating panel's window to the top without activating it. A slide-out that did not open is logged once.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DockingPanelRaiser
{
	private const uint SwpNoSize = 0x0001;
	private const uint SwpNoMove = 0x0002;
	private const uint SwpNoActivate = 0x0010;

	private readonly DockingManager _manager;
	private readonly DockingVisualTree _tree;
	private readonly Func<bool> _slideOutIsOpen;
	private bool _warned;

	public DockingPanelRaiser(DockingManager manager, DockingVisualTree tree, Func<bool> slideOutIsOpen)
	{
		_manager = manager;
		_tree = tree;
		_slideOutIsOpen = slideOutIsOpen;
	}

	public ILogger? Logger { get; set; }

	/// <summary>True when the slide-out is open afterwards.</summary>
	public bool SlideOut(LayoutAnchorable panel)
	{
		try
		{
			var anchor = _tree.Descendants<LayoutAnchorControl>(_manager).FirstOrDefault(a => ReferenceEquals(a.Model, panel.Parent));
			anchor?.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent, Source = anchor });
		}
		catch (Exception ex)
		{
			Warn(panel, ex);
			return false;
		}

		if (_slideOutIsOpen())
			return true;

		Warn(panel, null);
		return false;
	}

	/// <summary>Lifts the window of a floating panel to the top of the z-order; it is not activated.</summary>
	public void Raise(LayoutAnchorable panel)
	{
		foreach (var window in _manager.FloatingWindows.Where(w => w.Model is ILayoutContainer container && container.Descendents().Contains(panel)))
		{
			var handle = new WindowInteropHelper(window).Handle;
			if (handle != IntPtr.Zero)
				SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);   // HWND_TOP
		}
	}

	private void Warn(LayoutAnchorable panel, Exception? ex)
	{
		if (_warned)
			return;

		_warned = true;
		Logger?.LogWarning(ex, Constants.ShellLayout.SlideOutFailedLog, panel.ContentId);
	}

	[DllImport("user32.dll")]
	private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}
