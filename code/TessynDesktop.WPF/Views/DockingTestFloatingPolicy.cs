using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;
using TessynDesktop.WPF.Infrastructure;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// Floating panel windows under <c>--test-window</c> (FR.6.22, FR.29.26): like the test main window they lie off every monitor, are not in the taskbar, are never activated
/// (<c>ShowActivated</c> off, <c>WS_EX_NOACTIVATE</c>, the activation veto) and sit under the test-window focus guard, which also hands a foreground change back to the user's window.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DockingTestFloatingPolicy
{
	/// <summary>Configures the window before it is shown; the focus guard is applied as soon as it has a handle.</summary>
	public void Apply(Window window, ILogger? logger)
	{
		window.ShowActivated = false;
		window.ShowInTaskbar = false;
		window.WindowStartupLocation = WindowStartupLocation.Manual;
		window.Left = Constants.Limits.OffScreenCoordinate;
		window.Top = Constants.Limits.OffScreenCoordinate;
		window.SourceInitialized += (_, _) =>
		{
			window.ShowActivated = false;   // the library sets it again while it builds the window; this is read when the window is really shown
			window.ShowInTaskbar = false;
			TestWindowFocusGuard.Apply(new WindowInteropHelper(window).Handle);
		};
		window.Loaded += (_, _) => window.ShowActivated = false;
		System.ComponentModel.DependencyPropertyDescriptor.FromProperty(Window.ShowActivatedProperty, typeof(Window)).AddValueChanged(window, (_, _) =>
		{
			if (window.ShowActivated)
				window.ShowActivated = false;   // the library switches it on again
		});

		logger?.LogDebug(Constants.DockingTestHook.FloatingPlacedLog);
	}
}
