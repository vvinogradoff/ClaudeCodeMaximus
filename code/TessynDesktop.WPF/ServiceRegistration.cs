using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Transcript;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF;

/// <summary>The DI composition root.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ServiceRegistration
{
	public void Register(IServiceCollection services, AppPaths paths, Dispatcher dispatcher, StartupOptions options)
	{
		services.AddSingleton(paths);
		services.AddSingleton(options);
		services.AddSingleton<IUiDispatcher>(new WpfUiDispatcher(dispatcher));
		services.AddTransient<GlobalExceptionLogger>();

		// Daemon connection: daemon.json discovery (overridable with --daemon-file) and the real TessynClient.
		services.AddSingleton<IDaemonLocator>(new DaemonLocator(options.DaemonFile));
		services.AddSingleton<ITessynClientFactory, TessynClientFactory>();
		services.AddSingleton<ConnectionViewModel>();

		services.AddSingleton<TreeContext>();
		services.AddSingleton<IShellModule, TreeShellModule>();
		if (options.TestWindow)
			services.AddSingleton<IFolderPicker>(sp => new ScriptedFolderPicker(options.TestFolderAnswers, sp.GetRequiredService<ILogger<ScriptedFolderPicker>>()));
		else
			services.AddSingleton<IFolderPicker, WpfFolderPicker>();

		services.AddSingleton<Func<ITessynClient, TreeViewModel>>(sp => client =>
		{
			var tree = new TreeViewModel(
				client, sp.GetRequiredService<IUiDispatcher>(), sp.GetRequiredService<ILogger<TreeViewModel>>(), Constants.Timeouts.SearchDebounce,
				new TreeOperations(client, sp.GetRequiredService<IDialogService>(), sp.GetRequiredService<IFolderPicker>(), sp.GetRequiredService<ILogger<TreeOperations>>()),
				new TreeNodeInfoLoader(client, sp.GetRequiredService<ILogger<TreeNodeInfoLoader>>()));
			sp.GetRequiredService<TreeContext>().Current = tree;
			return tree;
		});
		services.AddSingleton<UrlPolicy>();
		services.AddSingleton<IUrlLauncher, SystemUrlLauncher>();
		services.AddSingleton<MediaPreviewPolicy>();
		services.AddSingleton<PrivateAddressCheck>();
		services.AddSingleton<MediaProxyUrl>();
		services.AddSingleton<WebRequestPolicy>();
		services.AddSingleton<RemoteMediaHttpClientFactory>();
		services.AddSingleton<RemoteMediaFetcher>(sp => new RemoteMediaFetcher(
			sp.GetRequiredService<RemoteMediaHttpClientFactory>().Create(), sp.GetRequiredService<MediaPreviewPolicy>(), sp.GetRequiredService<PrivateAddressCheck>()));
		services.AddSingleton<TranscriptMediaBroker>();
		services.AddSingleton<MediaLinkDetector>();
		services.AddSingleton<LocalPathDetector>();
		services.AddSingleton<ExternalOpenPolicy>();
		services.AddSingleton<FileKindClassifier>();
		services.AddSingleton<PreviewFileStore>();
		services.AddSingleton<IExternalFileLauncher, SystemExternalFileLauncher>();
		services.AddSingleton<IEntryRenderer, MarkdigEntryRenderer>();
		services.AddSingleton<IMarkdownRenderer>(sp => (MarkdigEntryRenderer)sp.GetRequiredService<IEntryRenderer>());
		services.AddSingleton<SwitchLatencyTracker>();
		services.AddSingleton<WebViewEnvironmentProvider>();
		services.AddSingleton<ITranscriptSurface, WebView2TranscriptSurface>();
		services.AddSingleton<WebView2TranscriptSurface>(sp => (WebView2TranscriptSurface)sp.GetRequiredService<ITranscriptSurface>());
		services.AddSingleton<FilePreviewSurface>();
		services.AddSingleton<IPreviewSurface>(sp => sp.GetRequiredService<FilePreviewSurface>());
		services.AddSingleton<Func<ITessynClient, SessionPaneViewModel>>(sp => client => new SessionPaneViewModel(
			client, sp.GetRequiredService<ITranscriptSurface>(), sp.GetRequiredService<IEntryRenderer>(), sp.GetRequiredService<IUrlLauncher>(),
			sp.GetRequiredService<IUiDispatcher>(), sp.GetRequiredService<SwitchLatencyTracker>(), sp.GetRequiredService<ILogger<SessionPaneViewModel>>(),
			Constants.Limits.MaxCachedTranscripts,
			new FocusViewModel(client, sp.GetRequiredService<IUiDispatcher>(), sp.GetRequiredService<ILogger<FocusViewModel>>()),
			new FilePreviewViewModel(
				new FilePreviewLoader(client, sp.GetRequiredService<FileKindClassifier>(), sp.GetRequiredService<PreviewFileStore>(), sp.GetRequiredService<ExternalOpenPolicy>()),
				sp.GetRequiredService<IPreviewSurface>(), sp.GetRequiredService<IMarkdownRenderer>(), sp.GetRequiredService<IExternalFileLauncher>(),
				sp.GetRequiredService<ExternalOpenPolicy>(), sp.GetRequiredService<PreviewFileStore>(), sp.GetRequiredService<IUrlLauncher>(),
				sp.GetRequiredService<IUiDispatcher>(), sp.GetRequiredService<ILogger<FilePreviewViewModel>>()),
			sp.GetRequiredService<MediaPreviewPolicy>(),
			sp.GetRequiredService<IClientStateStore>())
		{
			TestDropPaths = options.TestDropPaths,
		});
		services.AddSingleton<IClientStateStore, JsonClientStateStore>();

		// Shell foundations (W0). Singletons by nature: each is the one registry or store of its kind for the whole window.
		services.AddSingleton<IClientSettingsStore, ClientSettingsStore>();
		services.AddSingleton<IShellSlots, ShellSlots>();
		services.AddSingleton<IShellCommands, ShellCommands>();
		services.AddSingleton<ShellModuleLoader>();
		// A test window never opens a modal dialog: nobody could click it.
		if (options.TestWindow)
			services.AddSingleton<IDialogPresenter>(sp => new ScriptedDialogPresenter(options.TestDialogAnswers, sp.GetRequiredService<ILogger<ScriptedDialogPresenter>>()));
		else
			services.AddSingleton<IDialogPresenter, WpfDialogPresenter>();

		services.AddSingleton<IDialogService, DialogService>();
		services.AddSingleton<SessionListsHost>();
		services.AddSingleton<IShellModule, SessionListsModule>();
		services.AddSingleton<IShellModule, TurnLoopShellModule>();
		services.AddSingleton<ProfileViewModel>();
		services.AddSingleton<ProfileSwitchViewModel>();
		services.AddSingleton<ProfileHost>();
		services.AddSingleton<IShellModule, ProfileShellModule>();
		services.AddSingleton<WindowPlacementClamper>();
		services.AddSingleton<Win32WindowPlacement>();
		services.AddSingleton<IHostedService>(sp => new UiWatchdogService(dispatcher, sp.GetRequiredService<ILogger<UiStallMonitor>>()));
		services.AddSingleton<MainViewModel>();
		services.AddSingleton<MainWindow>();
	}
}
