using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF;

/// <summary>Application entry: single-instance mutex, Generic Host with DI, Serilog and global exception logging.</summary>
/// <remarks>Created by Claude</remarks>
public partial class App : Application
{
	private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(6);

	private IHost? _host;
	private Mutex? _singleInstanceMutex;

	/// <summary>The application's service provider (set once the host is built).</summary>
	public IServiceProvider Services => _host!.Services;

	protected override async void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);

		var startup = System.Diagnostics.Stopwatch.StartNew();
		var options = StartupOptions.Parse(e.Args);
		_singleInstanceMutex = new Mutex(initiallyOwned: true, options.MutexName, out var createdNew);
		if (!createdNew)
		{
			_singleInstanceMutex.Dispose();
			_singleInstanceMutex = null;
			Shutdown();
			return;
		}

		var paths = options.DataRoot == null ? new AppPaths() : new AppPaths(options.DataRoot);
		Log.Logger = new LoggingSetup().CreateLogger(paths);

		_host = Host.CreateDefaultBuilder()
			.UseSerilog(Log.Logger)
			.ConfigureServices(services => new ServiceRegistration().Register(services, paths, Dispatcher, options))
			.Build();

		var logger = _host.Services.GetRequiredService<ILogger<App>>();
		var hostBuilt = startup.ElapsedMilliseconds;
		_host.Services.GetRequiredService<GlobalExceptionLogger>().Attach(this);
		logger.LogInformation("TessynDesktop.WPF starting");

		// The WebView2 environment takes a second or more to create; start it now so it overlaps with the rest of the startup.
		_ = _host.Services.GetRequiredService<WebViewEnvironmentProvider>().GetAsync();

		await _host.StartAsync();
		await _host.Services.GetRequiredService<IClientStateStore>().LoadAsync();
		var stateLoaded = startup.ElapsedMilliseconds;

		var window = _host.Services.GetRequiredService<MainWindow>();
		var windowCreated = startup.ElapsedMilliseconds;
		MainWindow = window;
		window.Show();
		logger.LogInformation("Startup: host built {Host} ms, state loaded {State} ms, window created {Created} ms, shown {Shown} ms",
			hostBuilt, stateLoaded, windowCreated, startup.ElapsedMilliseconds);
	}

	protected override void OnExit(ExitEventArgs e)
	{
		if (_host != null)
		{
			// Never block the UI thread on work that may need it: run the shutdown on the thread pool with a bounded wait.
			var host = _host;
			var shutdown = Task.Run(async () =>
			{
				try
				{
					await host.Services.GetRequiredService<IClientStateStore>().FlushAsync();
					await host.Services.GetRequiredService<ConnectionViewModel>().DisposeAsync();
					await host.StopAsync(TimeSpan.FromSeconds(3));
				}
				catch (Exception ex)
				{
					Log.Error(ex, "Shutdown failed");
				}
			});
			shutdown.Wait(ShutdownTimeout);
			host.Dispose();
		}

		Log.CloseAndFlush();
		try
		{
			_singleInstanceMutex?.ReleaseMutex();
		}
		catch (ApplicationException)
		{
		}

		_singleInstanceMutex?.Dispose();
		base.OnExit(e);
	}
}
