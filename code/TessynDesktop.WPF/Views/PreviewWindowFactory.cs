using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// <see cref="IPreviewWindowFactory"/>: builds a <see cref="PreviewWindow"/> with its view model for the router (FR.29.5). Resolves everything when a window
/// is created, not in the constructor, so the router, this factory and the routing launcher can depend on each other without a cycle. Not owned by the main
/// window, so a preview window has its own taskbar button and stays where it is when the main window is minimized or moved.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewWindowFactory : IPreviewWindowFactory
{
	private readonly IServiceProvider _services;

	public PreviewWindowFactory(IServiceProvider services)
	{
		_services = services;
	}

	public IPreviewWindowHandle Create(PreviewGroupModel group, bool pinned, PreviewPlacementModel? ownPlacement)
	{
		var settings = Get<IPreviewSettingsService>();
		var viewModel = new PreviewWindowViewModel(group.Id, group.Name, pinned, settings, Get<IPreviewContentFactory>(), Get<IUiDispatcher>());
		var window = new PreviewWindow(
			viewModel, ownPlacement, settings, Get<IMonitorWatcher>(), _services.GetService<PreviewPlacementPlanner>() ?? new PreviewPlacementPlanner(),
			Get<Win32WindowPlacement>(), Get<AppearanceApplier>(), Get<StartupOptions>(), MainWindowHandle, Get<ILoggerFactory>().CreateLogger<PreviewWindow>(), Get<IUiDispatcher>());
		return new PreviewWindowHandle(viewModel, window);
	}

	/// <summary>The main window's handle, to tell which monitor it is on; zero when there is none yet.</summary>
	private static IntPtr MainWindowHandle() => Application.Current?.MainWindow is { } main ? new WindowInteropHelper(main).Handle : IntPtr.Zero;

	private T Get<T>() where T : notnull => _services.GetRequiredService<T>();
}
