using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Switch;
using TessynDesktop.WPF.Transcript;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.ViewModels.Settings;
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
		services.AddSingleton<IDaemonHostLauncher>(sp => CreateHostLauncher(sp, paths, options));
		services.AddSingleton<DaemonRestarter>(sp => CreateRestarter(sp, paths, options));
		services.AddSingleton<UpdateNoticeViewModel>();
		if (options.IsRealEnvironment && !SourceBuilds.IsBuildOutput(AppContext.BaseDirectory))
			services.AddSingleton<LiveUpdateService>(sp => CreateLiveUpdate(sp, paths, options));
		services.AddSingleton<ConnectionViewModel>(sp => new ConnectionViewModel(
			sp.GetRequiredService<IDaemonLocator>(), sp.GetRequiredService<ITessynClientFactory>(), sp.GetRequiredService<IUiDispatcher>(),
			sp.GetRequiredService<ILogger<ConnectionViewModel>>(), sp.GetRequiredService<IDaemonHostLauncher>(),
			RestarterFor(sp.GetRequiredService<IDaemonHostLauncher>(), () => sp.GetRequiredService<DaemonRestarter>())));

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
		services.AddSingleton<IVideoThumbnailService, ShellVideoThumbnailService>();
		// A test window never starts a media player or any other program: the launcher only logs the file it was asked to open.
		if (options.TestWindow)
			services.AddSingleton<IExternalFileLauncher, LoggingExternalFileLauncher>();
		else
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
				sp.GetRequiredService<IUiDispatcher>(), sp.GetRequiredService<ILogger<FilePreviewViewModel>>(), sp.GetRequiredService<IVideoThumbnailService>()),
			sp.GetRequiredService<MediaPreviewPolicy>(),
			sp.GetRequiredService<IClientStateStore>())
		{
			TestDropPaths = options.TestDropPaths,
			Dialogs = sp.GetRequiredService<IDialogService>(),
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
		services.AddSingleton<IShellModule, ToolbarShellModule>();
		services.AddSingleton<ProfileViewModel>();
		services.AddSingleton<ProfileSwitchViewModel>();
		services.AddSingleton<ProfileHost>();
		services.AddSingleton<IShellModule, ProfileShellModule>();
		services.AddSingleton<IShellModule, ComposerShellModule>();
		services.AddSingleton<IShellModule, ScheduleShellModule>();

		// W7: the import picker (and global search).
		services.AddSingleton<ImportPickerHost>();
		services.AddSingleton<IShellModule, ImportShellModule>();

		// W6: the Settings window. The editor window of an MCP server is shown by its own presenter, a test window has no folder dialog to click either.
		services.AddSingleton<IMcpServerEditorPresenter, WpfMcpServerEditorPresenter>();
		services.AddSingleton<Func<ITessynClient, SettingsViewModel>>(sp => client => new SettingsViewModel(
			client, sp.GetRequiredService<IClientSettingsStore>(), sp.GetRequiredService<IShellCommands>(), sp.GetRequiredService<IDialogService>(),
			sp.GetRequiredService<IFolderPicker>(), sp.GetRequiredService<IMcpServerEditorPresenter>(), sp.GetRequiredService<ProfileViewModel>(),
			sp.GetRequiredService<IUiDispatcher>(), sp.GetRequiredService<ILoggerFactory>(), sp.GetRequiredService<IAppearanceService>()));
		services.AddSingleton<SettingsHost>();
		services.AddSingleton<IShellModule, SettingsShellModule>();

		// W10a: theme and fonts, the custom title bar, the status bar items, the screensaver.
		services.AddSingleton<ISystemThemeProbe, RegistrySystemThemeProbe>();
		services.AddSingleton<IAppearanceService, AppearanceService>();
		services.AddSingleton<AppearanceApplier>();
		services.AddSingleton<ColourApplier>();
		services.AddSingleton<ListDisplayService>();
		services.AddSingleton<IScreensaverImages, FolderScreensaverImages>();
		services.AddSingleton(sp => new ScreensaverController(sp.GetRequiredService<IClientSettingsStore>(), sp.GetRequiredService<IScreensaverImages>(), DateTime.UtcNow));
		services.AddSingleton(sp => new TitleBarViewModel(sp.GetRequiredService<IAppearanceService>(), sp.GetRequiredService<IClientSettingsStore>(), sp.GetRequiredService<IShellCommands>(),
			sp.GetRequiredService<IUiDispatcher>(), BuildNumber.Read(typeof(App).Assembly), SourceBuilds.IsBuildOutput(AppContext.BaseDirectory)));
		services.AddSingleton<StatusBarViewModel>();
		services.AddSingleton<IShellModule, ChromeShellModule>();

		// W1b: the completion sound. A test window never plays one (the sound is the one thing an off-screen window cannot hide).
		if (options.TestWindow)
			services.AddSingleton<ISoundPlayer, SilentSoundPlayer>();
		else
			services.AddSingleton<ISoundPlayer, WinmmSoundPlayer>();

		services.AddSingleton<IHostedService, CompletionSoundHost>();

		services.AddSingleton<WindowPlacementClamper>();
		services.AddSingleton<Win32WindowPlacement>();
		services.AddSingleton<IHostedService>(sp => new UiWatchdogService(dispatcher, sp.GetRequiredService<ILogger<UiStallMonitor>>()));
		services.AddSingleton<MainViewModel>();
		services.AddSingleton<MainWindow>();
	}

	/// <summary>
	/// The restarter is offered only where the client can bring a host back: with the real launcher. A client run from a build output (rule A15), or in a custom environment,
	/// has the no-op launcher, and a restart there would end the daemon and leave nobody to start one.
	/// </summary>
	public static DaemonRestarter? RestarterFor(IDaemonHostLauncher launcher, Func<DaemonRestarter> create) => launcher is DaemonHostLauncher ? create() : null;

	/// <summary>The "daemon is older than the build" check and the one-click restart: only in the real environment and from the run folder.</summary>
	private static DaemonRestarter CreateRestarter(IServiceProvider sp, AppPaths paths, StartupOptions options)
	{
		var sourceLocation = new SourceBuilds(Path.Combine(paths.DataRoot, Constants.Switch.AppSettingsFileName)).ReadSourceLocation();
		var scriptDirectory = Path.GetTempPath();
		var exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, Constants.Switch.WpfExeName);
		var restarter = new ScriptClientRestarter(new PowerShellScriptLauncher(), scriptDirectory, exe, options.Arguments, Environment.ProcessId,
			() =>
			{
				sp.GetService<LiveUpdateService>()?.MarkRestarting(); // a restart script is waiting: the exit must not start a copy-only script beside it
				System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => System.Windows.Application.Current.Shutdown()));
			});
		return new DaemonRestarter(sourceLocation, new ProcessDaemonBuildProbe(), sp.GetRequiredService<IDialogService>(), new NamedEventHostExitSignal(),
			new ProcessExitWaiter(), restarter, Constants.Switch.DaemonExitTimeout, sp.GetRequiredService<ILoggerFactory>().CreateLogger<DaemonRestarter>(),
			Path.Combine(AppContext.BaseDirectory.TrimEnd('\\', '/'), Constants.Switch.HostFolderName));
	}

	/// <summary>The running client's update check, notice and copy-on-exit: only in the real environment and from the run folder.</summary>
	private static LiveUpdateService CreateLiveUpdate(IServiceProvider sp, AppPaths paths, StartupOptions options)
	{
		var sourceLocation = new SourceBuilds(Path.Combine(paths.DataRoot, Constants.Switch.AppSettingsFileName)).ReadSourceLocation();
		var loggers = sp.GetRequiredService<ILoggerFactory>();
		var updater = new WpfSelfUpdater(AppContext.BaseDirectory, sourceLocation, new PowerShellScriptLauncher(), Path.GetTempPath(), loggers.CreateLogger<WpfSelfUpdater>(), new MutexHostPresence());
		return new LiveUpdateService(updater, sp.GetRequiredService<UpdateNoticeViewModel>(), sp.GetRequiredService<IUiDispatcher>(), options.Arguments,
			() => System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => System.Windows.Application.Current.Shutdown())),
			Constants.Switch.UpdateCheckInterval, Environment.ProcessId, loggers.CreateLogger<LiveUpdateService>());
	}

	/// <summary>The launcher of the Avalonia background host (A23 Q6): real only in the real environment and only when running from the run folder (rule A15).</summary>
	private static IDaemonHostLauncher CreateHostLauncher(IServiceProvider sp, AppPaths paths, StartupOptions options)
	{
		var runFolder = AppContext.BaseDirectory.TrimEnd('\\', '/');
		if (!options.AutoStartHostEnabled || SourceBuilds.IsBuildOutput(runFolder))
			return new NoDaemonHostLauncher();

		var sourceLocation = new SourceBuilds(Path.Combine(paths.DataRoot, Constants.Switch.AppSettingsFileName)).ReadSourceLocation();
		return new DaemonHostLauncher(runFolder, sourceLocation, new ProcessHostStarter(), new MutexHostPresence(), TimeProvider.System,
			Constants.Switch.HostStartCooldown, sp.GetRequiredService<ILoggerFactory>().CreateLogger<DaemonHostLauncher>(),
			isEnabled: IsHostStartEnabled(sp.GetRequiredService<IClientSettingsStore>()));
	}

	/// <summary>The setting "Start the background daemon when none is running", read at every call so a change in Settings counts at once (off by default).</summary>
	public static Func<bool> IsHostStartEnabled(IClientSettingsStore store) => () => store.Get<DaemonHostSettings>().StartBackgroundDaemon;
}
