using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ClaudeMaximus.Services;
using ClaudeMaximus.ViewModels;
using ClaudeMaximus.Views;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;
using System.IO;
using System.Threading.Tasks;

namespace ClaudeMaximus;

public partial class App : Application
{
	public static IServiceProvider Services { get; private set; } = null!;

	public override void Initialize()
	{
		AvaloniaXamlLoader.Load(this);
	}

	public override void OnFrameworkInitializationCompleted()
	{
		ConfigureLogging();

		var services = new ServiceCollection();
		ConfigureServices(services);
		Services = services.BuildServiceProvider();

		var appSettings = Services.GetRequiredService<IAppSettingsService>();
		appSettings.Load();

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
			Services.GetRequiredService<IAgentMcpServer>().Start();
			Services.GetRequiredService<ISchedulerService>().Start();
			Services.GetRequiredService<ISkillProvisioningService>().ProvisionAll();
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
				selfUpdate.CheckAndTriggerUpdate();
				if (appSettings.Settings.AgentToolsEnabled)
				{
					Services.GetRequiredService<IAgentMcpServer>().Stop();
					Services.GetRequiredService<ISchedulerService>().Stop();
				}
			};

			// Build/refresh the cross-project session search index in the background (Stage 6).
			_ = Task.Run(() => Services.GetRequiredService<IDaemonClient>().ReindexSessionsAsync());
		}

		base.OnFrameworkInitializationCompleted();
	}

	private static void ConfigureLogging()
	{
		var logDir = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
			Constants.AppDataFolderName,
			"logs");

		Directory.CreateDirectory(logDir);

		Log.Logger = new LoggerConfiguration()
			.MinimumLevel.Debug()
			.WriteTo.File(
				Path.Combine(logDir, "log-.txt"),
				rollingInterval: RollingInterval.Day,
				retainedFileCountLimit: 7,
				outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
			.CreateLogger();

		Log.Information("ClaudeMaximus starting up. Logs: {LogDir}", logDir);
	}

	private static void ConfigureServices(IServiceCollection services)
	{
		services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
		services.AddSingleton<IAppSettingsService, AppSettingsService>();
		services.AddSingleton<IDirectoryLabelService, DirectoryLabelService>();
		services.AddSingleton<ISessionFileService, SessionFileService>();
		services.AddSingleton<IDraftService, DraftService>();
		services.AddSingleton<IClaudeProcessManager, ClaudeProcessManager>();
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
		services.AddSingleton<ISessionRegistry, SessionRegistry>();
		services.AddSingleton<SessionTreeViewModel>();

		// Agent orchestration singletons (FR.14, FR.15).
		// Lazy<T> registrations break the circular dependency between ISessionTurnService ↔ IAgentMcpServer.
		services.AddSingleton<ISessionTurnService, SessionTurnService>();
		services.AddSingleton<IAgentMcpServer, AgentMcpServer>();
		services.AddSingleton<ISchedulerService, SchedulerService>();
		services.AddSingleton<ISkillProvisioningService, SkillProvisioningService>();
		services.AddSingleton<INotificationService, WindowsNotificationService>();
		services.AddSingleton(sp => new Lazy<ISessionTurnService>(sp.GetRequiredService<ISessionTurnService>));
		services.AddSingleton(sp => new Lazy<IAgentMcpServer>(sp.GetRequiredService<IAgentMcpServer>));
		services.AddSingleton(sp => new Lazy<ISchedulerService>(sp.GetRequiredService<ISchedulerService>));

		// UI/daemon boundary (Stage 4): the UI talks to the daemon-owned domain through IDaemonClient.
		// In-process today; swapped for a TDCP wire adapter in Stage 5 with no ViewModel changes.
		services.AddSingleton<IDaemonClient, InProcDaemonClient>();

		// TDCP control surface (Stage 5a): co-hosted with AgentMcpServer on the /tdcp path.
		// Token is generated once on first run and persisted so restarts keep the same value.
		services.AddSingleton(sp =>
		{
			var appSettings = sp.GetRequiredService<IAppSettingsService>();
			if (string.IsNullOrEmpty(appSettings.Settings.TdcpToken))
			{
				appSettings.Settings.TdcpToken = Guid.NewGuid().ToString("N");
				appSettings.Save();
			}

			return new TdcpServer(sp.GetRequiredService<IDaemonClient>(), appSettings.Settings.TdcpToken);
		});

		services.AddSingleton<MainWindowViewModel>();
		services.AddTransient<SettingsViewModel>();
	}
}
