using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ClaudeMaximus.Services;
using ClaudeMaximus.ViewModels;
using ClaudeMaximus.Views;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
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

		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			desktop.MainWindow = new MainWindow
			{
				DataContext = Services.GetRequiredService<MainWindowViewModel>(),
			};

			desktop.Exit += (_, _) =>
			{
				selfUpdate.CheckAndTriggerUpdate();
				Services.GetRequiredService<ITessynDaemonService>().Dispose();
			};

			// Start daemon connection in background (non-blocking)
			_ = Task.Run(() => StartTessynConnectionAsync(appSettings));
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

		Log.Information("Tessyn Desktop starting up. Logs: {LogDir}", logDir);
	}

	private static async Task StartTessynConnectionAsync(IAppSettingsService appSettings)
	{
		try
		{
			// Check if tessyn is installed
			if (!IsTessynInstalled(appSettings.Settings.TessynPath))
			{
				Log.Warning("Tessyn daemon not found on PATH. Install with: npm install -g tessyn");
				var mainVm = Services.GetRequiredService<MainWindowViewModel>();
				Avalonia.Threading.Dispatcher.UIThread.Post(() =>
					mainVm.SetDaemonMissing("Tessyn daemon not found. Install with: npm install -g tessyn"));
				return;
			}

			var daemon = Services.GetRequiredService<ITessynDaemonService>();

			if (appSettings.Settings.AutoStartDaemon)
				await EnsureDaemonRunningAsync(appSettings.Settings.TessynPath);

			await daemon.ConnectAsync();
		}
		catch (Exception ex)
		{
			Log.Warning(ex, "Failed to start Tessyn daemon connection (will auto-reconnect)");
		}
	}

	private static bool IsTessynInstalled(string tessynPath)
	{
		try
		{
			var startInfo = new ProcessStartInfo
			{
				FileName = tessynPath,
				Arguments = "--version",
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true,
			};
			using var proc = Process.Start(startInfo);
			if (proc == null) return false;
			proc.WaitForExit(3000);
			return proc.ExitCode == 0;
		}
		catch
		{
			return false;
		}
	}

	private static async Task EnsureDaemonRunningAsync(string tessynPath)
	{
		var port = TessynDaemonService.GetPort();

		if (IsDaemonListening(port))
		{
			Log.Debug("Tessyn daemon already running on port {Port}", port);
			return;
		}

		Log.Information("Starting Tessyn daemon: {Path}", tessynPath);
		try
		{
			var startInfo = new ProcessStartInfo
			{
				FileName = tessynPath,
				CreateNoWindow = true,
				UseShellExecute = false,
				RedirectStandardOutput = false,
				RedirectStandardError = false,
			};

			var process = Process.Start(startInfo);
			if (process == null)
			{
				Log.Warning("Failed to start Tessyn daemon process");
				return;
			}

			// Detach: don't keep a reference, let it run independently
			process.Dispose();

			// Wait for daemon to become available
			var elapsed = 0;
			while (elapsed < Constants.Tessyn.DaemonStartupWaitMs)
			{
				await Task.Delay(Constants.Tessyn.DaemonStartupPollIntervalMs);
				elapsed += Constants.Tessyn.DaemonStartupPollIntervalMs;

				if (IsDaemonListening(port))
				{
					Log.Information("Tessyn daemon started and listening on port {Port}", port);
					return;
				}
			}

			Log.Warning("Tessyn daemon started but not listening after {WaitMs}ms", Constants.Tessyn.DaemonStartupWaitMs);
		}
		catch (Exception ex)
		{
			Log.Warning(ex, "Failed to start Tessyn daemon at path '{Path}'", tessynPath);
		}
	}

	private static bool IsDaemonListening(int port)
	{
		try
		{
			using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
			socket.Connect("127.0.0.1", port);
			return true;
		}
		catch (SocketException)
		{
			return false;
		}
	}

	private static void ConfigureServices(IServiceCollection services)
	{
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
		services.AddSingleton<ITessynDaemonService, TessynDaemonService>();
		services.AddSingleton<ITessynRunService, TessynRunService>();
		services.AddSingleton<SessionTreeViewModel>();
		services.AddSingleton<MainWindowViewModel>();
		services.AddTransient<SettingsViewModel>();
	}
}
