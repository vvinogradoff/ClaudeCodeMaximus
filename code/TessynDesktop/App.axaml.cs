using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TessynDesktop.Services;
using TessynDesktop.ViewModels;
using TessynDesktop.Views;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using TessynDesktop.Models;
using System;
using System.IO;
using System.Threading.Tasks;

namespace TessynDesktop;

public partial class App : Application
{
	public static IServiceProvider Services { get; private set; } = null!;

	public override void Initialize()
	{
		AvaloniaXamlLoader.Load(this);
	}

	public override void OnFrameworkInitializationCompleted()
	{
		// OnFrameworkInitializationCompleted runs on the UI thread; remember it for the log enricher, watchdog and inspector.
		var uiThread = new UiThreadTracker();
		uiThread.CaptureCurrentThread();

		var logLevel = new LoggingLevelSwitch(LogEventLevel.Information);
		ConfigureLogging(uiThread, logLevel);
		ConfigureGlobalExceptionHandlers();

		var services = new ServiceCollection();
		ConfigureServices(services, uiThread);
		Services = services.BuildServiceProvider();

		var appSettings = Services.GetRequiredService<IAppSettingsService>();
		appSettings.Load();

		// Verbose logging is read once, after settings load.
		var diagnosticsSettings = appSettings.Settings.Diagnostics;
		if (diagnosticsSettings.VerboseLogging)
			logLevel.MinimumLevel = LogEventLevel.Debug;
		if (diagnosticsSettings.Enabled)
		{
			Services.GetRequiredService<IProcessMetricsSampler>().Start();
			Services.GetRequiredService<IUiHangWatchdog>().Start();
		}

		var keyBindings = Services.GetRequiredService<IKeyBindingService>();
		keyBindings.EnsureDefaults();

		// One-time identity migration: populate ExternalId from ClaudeSessionId
		var migration = new Services.SessionIdentityMigration(appSettings);
		migration.MigrateAll();

		ThemeApplicator.Apply(appSettings.Settings);

		var selfUpdate = Services.GetRequiredService<ISelfUpdateService>();
		selfUpdate.Initialize();

		// Start agent MCP server and scheduler (FR.14, FR.15).
		if (appSettings.Settings.AgentToolsEnabled)
		{
			var mcpServer = Services.GetRequiredService<IAgentMcpServer>();
			mcpServer.Start();
			// Publish endpoint, pid and run id for client processes (the token stays in its DPAPI file).
			if (mcpServer.Port > 0)
				Services.GetRequiredService<DaemonDiscoveryFile>().Write(mcpServer.Port);
			Services.GetRequiredService<ISchedulerService>().Start();
			Services.GetRequiredService<ISkillProvisioningService>().ProvisionAll();
		}

		// Start the Telegram Bridge (FR.21) — independent toggle from AgentToolsEnabled.
		if (appSettings.Settings.TelegramEnabled)
			Services.GetRequiredService<ITelegramBridgeService>().Start();

		// Remote API (web client): publish the daemon's events as remote notifications. Never allowed to break start-up.
		try
		{
			Services.GetRequiredService<IRemoteNotificationPublisher>().Start();
		}
		catch (Exception ex)
		{
			Log.Error(ex, "Could not start the remote notification publisher");
		}

		// Web relay link: independent of AgentToolsEnabled; a no-op unless web-relay.dat is present and enabled.
		try
		{
			Services.GetRequiredService<IRelayConnectorService>().Start();
		}
		catch (Exception ex)
		{
			Log.Error(ex, "Could not start the relay connector");
		}

		// Wire toast click-to-activate before the window opens so early clicks are not missed (FR.16).
		var mainVm = Services.GetRequiredService<MainWindowViewModel>();
		var notifications = Services.GetRequiredService<INotificationService>();
		notifications.RegisterActivationHandler(nodeId =>
			Avalonia.Threading.Dispatcher.UIThread.Post(() => mainVm.SelectSessionByNodeId(nodeId)));

		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			desktop.MainWindow = new MainWindow
			{
				DataContext = mainVm,
			};

			desktop.Exit += (_, _) =>
			{
				if (diagnosticsSettings.Enabled)
				{
					Services.GetRequiredService<IUiHangWatchdog>().Dispose();
					Services.GetRequiredService<IProcessMetricsSampler>().Dispose();
				}
				selfUpdate.CheckAndTriggerUpdate();
				Services.GetRequiredService<ISessionIndexUpdater>().Stop();
				if (appSettings.Settings.AgentToolsEnabled)
				{
					Services.GetRequiredService<DaemonDiscoveryFile>().Clear();
					Services.GetRequiredService<IAgentMcpServer>().Stop();
					Services.GetRequiredService<ISchedulerService>().Stop();
				}
				if (appSettings.Settings.TelegramEnabled)
					Services.GetRequiredService<ITelegramBridgeService>().Stop();
				try
				{
					Services.GetRequiredService<IRelayConnectorService>().Stop();
				}
				catch (Exception ex)
				{
					Log.Error(ex, "Could not stop the relay connector");
				}
			};

			// Build/refresh the cross-project session search index in the background (Stage 6).
			_ = Task.Run(() => Services.GetRequiredService<IDaemonClient>().ReindexSessionsAsync());
			Services.GetRequiredService<ISessionIndexUpdater>().Start();
		}

		base.OnFrameworkInitializationCompleted();
	}

	private static void ConfigureGlobalExceptionHandlers()
	{
		var exceptions = new GlobalExceptionLogger();
		AppDomain.CurrentDomain.UnhandledException += exceptions.OnAppDomainUnhandled;
		TaskScheduler.UnobservedTaskException += exceptions.OnUnobservedTaskException;
		// Log only: the exception is deliberately not marked Handled, so behaviour is unchanged.
		Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) => exceptions.OnUiThreadException(e.Exception);
	}

	private static void ConfigureLogging(IUiThreadTracker uiThread, LoggingLevelSwitch logLevel)
	{
		var logDir = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
			Constants.AppDataFolderName,
			Constants.Diagnostics.LogsFolderName);

		Directory.CreateDirectory(logDir);

		Log.Logger = new LoggerConfiguration()
			.MinimumLevel.ControlledBy(logLevel)
			.Enrich.With(new UiThreadLogEventEnricher(uiThread))
			.WriteTo.File(
				Path.Combine(logDir, Constants.Diagnostics.LogFileName),
				rollingInterval: RollingInterval.Day,
				fileSizeLimitBytes: Constants.Diagnostics.LogFileSizeLimitBytes,
				rollOnFileSizeLimit: true,
				retainedFileCountLimit: Constants.Diagnostics.LogRetainedFileCount,
				outputTemplate: Constants.Diagnostics.LogOutputTemplate)
			.CreateLogger();

		Log.Information("TessynDesktop starting up. Logs: {LogDir}", logDir);
	}

	private static void ConfigureServices(IServiceCollection services, IUiThreadTracker uiThread)
	{
		services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();

		// D1 diagnostics. Singletons because each owns shared state or a background thread/timer: the UI-thread id holder
		// (read by the log enricher, watchdog, timer and inspector), the operation timer (the current UI operation), the
		// inspector (its one-capture-at-a-time guard), the sampler (timer + CPU baseline) and the watchdog (its thread).
		// Appsettings are already loaded when these are first resolved, so the settings section is the loaded instance.
		services.AddSingleton(uiThread);
		services.AddSingleton(sp => sp.GetRequiredService<IAppSettingsService>().Settings.Diagnostics ?? new DiagnosticsSettingsModel());
		services.AddSingleton<IOperationTimer, OperationTimer>();
		services.AddSingleton<IRuntimeInspector, ClrMdRuntimeInspector>();
		services.AddSingleton<IProcessMetricsSampler, ProcessMetricsSampler>();
		services.AddSingleton<IUiHangWatchdog, UiHangWatchdog>();
		services.AddTransient<IDiagnosticsArtifactStore, DiagnosticsArtifactStore>();
		services.AddTransient<DiagnosticsToolHandler>();
		services.AddSingleton<IAppSettingsService, AppSettingsService>();
		services.AddSingleton<IDirectoryLabelService, DirectoryLabelService>();
		services.AddSingleton<ISessionFileService, SessionFileService>();
		// FR.23 — Singletons: the service owns the ledger cache and serialises writes (one instance shared by
		// AgentMcpServer, SessionTurnService and InProcDaemonClient); the store is stateless but shared with it.
		services.AddSingleton<AttentionLedgerStore>();
		services.AddSingleton<IAttentionService, AttentionService>();
		// FR.24 — Singletons for the same reason: the service owns the tag cache, the workspace map and the write lock.
		services.AddSingleton<SessionTagStore>();
		services.AddSingleton<ISessionTagService, SessionTagService>();
		services.AddSingleton<IDraftService, DraftService>();
		services.AddSingleton<ClaudeProcessLauncher>();
		services.AddSingleton<IClaudeProcessManager, ClaudeProcessManager>();
		services.AddSingleton<IClaudeStreamingSessionManager, ClaudeStreamingSessionManager>();
		services.AddSingleton<ISelfUpdateService, SelfUpdateService>();
		services.AddSingleton<IClaudeSessionStatusService, ClaudeSessionStatusService>();
		services.AddSingleton<ISessionSearchService, SessionSearchService>();
		services.AddSingleton<IGitOriginService, GitOriginService>();
		services.AddSingleton<ICodeIndexService, CodeIndexService>();
		services.AddSingleton<IClaudeSessionImportService, ClaudeSessionImportService>();
		services.AddSingleton<IClaudeAssistService, ClaudeAssistService>();
		services.AddSingleton<IKeyBindingService, KeyBindingService>();
		services.AddSingleton<IClaudeProfileService, ClaudeProfileService>();
		services.AddSingleton<IOllamaModelService, OllamaModelService>();
		services.AddSingleton<IClaudeModelService, ClaudeModelService>();
		services.AddSingleton<IClaudeUsageService, ClaudeUsageService>();
		services.AddSingleton<IClaudeSessionIndexService, ClaudeSessionIndexService>();
		services.AddSingleton<ISessionIndexUpdater, SessionIndexUpdater>();
		services.AddSingleton<ISessionRegistry, SessionRegistry>();
		services.AddSingleton<SessionTreeViewModel>();

		// External MCP Server Registry (FR.20).
		services.AddSingleton<System.Net.Http.HttpClient>();
		services.AddSingleton<IMcpTokenStore, McpTokenStore>();
		services.AddSingleton<IMcpOAuthService, McpOAuthService>();
		services.AddSingleton<IMcpConfigMerger, McpConfigMerger>();

		// Agent orchestration singletons (FR.14, FR.15).
		// Lazy<T> registrations break the circular dependency between ISessionTurnService ↔ IAgentMcpServer.
		services.AddSingleton<ISessionTurnService, SessionTurnService>();
		services.AddSingleton<ISessionCompactionService, SessionCompactionService>();
		services.AddSingleton<ITurnEngine, TurnEngine>();
		services.AddSingleton<IAgentMcpServer, AgentMcpServer>();
		services.AddSingleton<ISchedulerService, SchedulerService>();
		services.AddSingleton<ISkillProvisioningService, SkillProvisioningService>();
		services.AddSingleton<INotificationService, WindowsNotificationService>();
		services.AddSingleton<ISessionSoundService, SessionSoundService>();
		services.AddSingleton(sp => new Lazy<ISessionTurnService>(sp.GetRequiredService<ISessionTurnService>));
		services.AddSingleton(sp => new Lazy<IAgentMcpServer>(sp.GetRequiredService<IAgentMcpServer>));
		services.AddSingleton(sp => new Lazy<ISchedulerService>(sp.GetRequiredService<ISchedulerService>));

		// Telegram Notification Bridge (FR.21). Lazy<ITelegramBridgeService> breaks the circular
		// dependency between ISessionTurnService (topic-ensure hook) ↔ ITelegramBridgeService (turn runner).
		services.AddSingleton<ITelegramTokenStore, TelegramTokenStore>();
		services.AddSingleton<ITelegramClient, TelegramClient>();
		services.AddSingleton<ITelegramBridgeService, TelegramBridgeService>();
		services.AddSingleton(sp => new Lazy<ITelegramBridgeService>(sp.GetRequiredService<ITelegramBridgeService>));

		// UI/daemon boundary (Stage 4): the UI talks to the daemon-owned domain through IDaemonClient.
		// In-process today; swapped for a TDCP wire adapter in Stage 5 with no ViewModel changes.
		services.AddSingleton<IDaemonClient, InProcDaemonClient>();

		// TDCP control surface (Stage 5a): co-hosted with AgentMcpServer on the /tdcp path.
		// The token is generated once on first run and kept in a DPAPI-protected file (never in appsettings.json); a token from an
		// older appsettings.json is moved there once so it does not change. daemon.json tells client processes where to connect.
		services.AddSingleton<ITdcpTokenStore, TdcpTokenStore>();
		services.AddSingleton<DaemonDiscoveryFile>();
		services.AddSingleton(sp =>
		{
			var token = TdcpTokenProvider.GetOrCreate(sp.GetRequiredService<ITdcpTokenStore>(), sp.GetRequiredService<IAppSettingsService>());
			return new TdcpServer(sp.GetRequiredService<IDaemonClient>(), sp.GetRequiredService<TdcpDispatcher>(), token,
				sp.GetRequiredService<IEventLog>(), sp.GetRequiredService<IDaemonRunInfo>());
		});

		// Remote API foundation (web client). Singletons, each because it owns state that must be shared and survive the call:
		// the dispatcher (the method table built once from every IRemoteRpcModule), ClientRpcModule (the per-run id client.hello
		// reports), the event bus (subscribers and pending coalescing timers), the tree-version and epoch trackers (counters shared
		// by notifications and queries), the activity service (its per-file cache) and the publisher (its event subscriptions).
		// The loopback module and the state builder hold no state, so they are transient.
		services.AddTransient<IRemoteRpcModule, TdcpLoopbackRpcModule>();
		services.AddSingleton<IRemoteRpcModule, ClientRpcModule>(); // gets the shared IDaemonRunInfo and IEventLog from DI
		services.AddSingleton<TdcpDispatcher>();
		services.AddTransient<IRemoteRpcModule, SessionCompactRpcModule>();
		services.AddSingleton<IRemoteEventBus, RemoteEventBus>();
		services.AddSingleton<IEventLog, EventLog>();
		services.AddSingleton<IDaemonRunInfo, DaemonRunInfo>();
		services.AddSingleton<ITreeVersionTracker, TreeVersionTracker>();
		services.AddSingleton<ISessionEpochTracker, SessionEpochTracker>();
		services.AddSingleton<ISessionActivityService, SessionActivityService>();
		services.AddTransient<ISessionStateBuilder, SessionStateBuilder>();
		services.AddTransient<IRemoteTreeReader, RemoteTreeReader>();
		services.AddTransient<IRemoteRpcModule, TreeRemoteRpcModule>();
		services.AddTransient<IRemoteRpcModule, RecentRemoteRpcModule>();
		services.AddTransient<IRemoteRpcModule, TagsRemoteRpcModule>();
		services.AddSingleton<IRemoteNotificationPublisher, RemoteNotificationPublisher>();

		// Web relay link. Singletons: the connector owns the connect loop, its status and its bus subscription;
		// the config store and HTTP factory are stateless but shared with the Settings window.
		services.AddSingleton<IRelayConfigStore, RelayConfigStore>();
		services.AddSingleton<IRelayHttpClientFactory, RelayHttpClientFactory>();
		services.AddSingleton<IRelayConnectorService, RelayConnectorService>();

		// Remote session, attention, catalog and usage methods (web client). Singletons only where state must be shared: the
		// Default profile label (its cached resolution), the parsed-entry cache, the pending-send counters and the per-profile
		// usage cache with its desktop-poller subscription. The fetcher owns one HttpClient. Everything else is stateless.
		services.AddSingleton<IProfileLabelService, ProfileLabelService>();
		services.AddSingleton<ISessionContentStore, SessionContentStore>();
		services.AddSingleton<ISessionEntryReader, SessionEntryReader>();
		services.AddSingleton<ISessionSendService, SessionSendService>();
		services.AddSingleton<IClaudeUsageFetcher, ClaudeUsageFetcher>();
		services.AddSingleton<IRemoteUsageService, RemoteUsageService>();
		services.AddTransient<ITurnOptionsResolver, TurnOptionsResolver>();
		services.AddTransient<IAttentionReplyService, AttentionReplyService>();
		services.AddTransient<IRemoteRpcModule, SessionRpcModule>();
		services.AddTransient<IRemoteRpcModule, AttentionRpcModule>();
		services.AddTransient<IRemoteRpcModule, CatalogRpcModule>();
		services.AddTransient<IRemoteRpcModule, UsageRpcModule>();

		services.AddSingleton<MainWindowViewModel>();
		services.AddTransient<SettingsViewModel>();
	}
}
