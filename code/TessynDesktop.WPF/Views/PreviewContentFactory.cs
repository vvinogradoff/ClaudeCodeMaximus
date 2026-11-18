using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// <see cref="IPreviewContentFactory"/> over the service provider. Everything is resolved when a window asks (never in the constructor), so there is no cycle
/// between the router, the window factory and the routing <see cref="IUrlLauncher"/>. A file renderer is built like the side pane's, with the registered
/// <see cref="IUrlLauncher"/> (links inside a preview route, FR.29.4) but with its own <see cref="FilePreviewSurface"/>; a browser gets its own
/// <see cref="BrowserSurface"/> and the system browser launcher itself, which never routes (FR.27.4). A type nobody registered yet is constructed here.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewContentFactory : IPreviewContentFactory
{
	private readonly IServiceProvider _services;
	private BrowserEnvironmentProvider? _browserEnvironment;
	private BrowserNavigationPolicy? _browserPolicy;

	public PreviewContentFactory(IServiceProvider services)
	{
		_services = services;
	}

	public FilePreviewViewModel CreateFile(ITessynClient client)
	{
		var surface = ActivatorUtilities.CreateInstance<FilePreviewSurface>(_services);
		return new FilePreviewViewModel(
			new FilePreviewLoader(client, Get<FileKindClassifier>(), Get<PreviewFileStore>(), Get<ExternalOpenPolicy>()),
			surface, Get<IMarkdownRenderer>(), Get<IExternalFileLauncher>(), Get<ExternalOpenPolicy>(), Get<PreviewFileStore>(), Get<IUrlLauncher>(),
			Get<IUiDispatcher>(), Get<ILogger<FilePreviewViewModel>>(), Get<IVideoThumbnailService>(), ownsSurface: true);
	}

	public PreviewBrowserContent CreateBrowser()
	{
		var surface = new BrowserSurface(
			_services.GetService<BrowserEnvironmentProvider>() ?? (_browserEnvironment ??= ActivatorUtilities.CreateInstance<BrowserEnvironmentProvider>(_services)),
			_services.GetService<BrowserNavigationPolicy>() ?? (_browserPolicy ??= new BrowserNavigationPolicy()),
			Get<StartupOptions>(), Get<ILogger<BrowserSurface>>());
		var systemBrowser = Get<SystemUrlLauncher>();   // the concrete launcher: a link in the address bar's "Open in system browser" never routes
		var viewModel = new BrowserPreviewViewModel(surface, Get<UrlPolicy>(), systemBrowser, Get<IUiDispatcher>(), Get<ILogger<BrowserPreviewViewModel>>());
		return new PreviewBrowserContent(viewModel, surface);
	}

	private T Get<T>() where T : notnull => _services.GetRequiredService<T>();
}
