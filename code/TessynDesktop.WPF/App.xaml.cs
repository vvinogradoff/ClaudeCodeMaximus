using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
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
	private SingleInstanceGuard? _singleInstance;
	private bool _activationPending;

	/// <summary>The application's service provider (set once the host is built).</summary>
	public IServiceProvider Services => _host!.Services;

	protected override async void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);

		var startup = System.Diagnostics.Stopwatch.StartNew();
		var options = StartupOptions.Parse(e.Args);
		_singleInstance = new SingleInstanceGuard(options.MutexName);
		if (!_singleInstance.TryAcquire())
		{
			// The running instance has been asked to come to the front.
			_singleInstance = null;
			Shutdown();
			return;
		}

		_singleInstance.ListenForActivation(() => Dispatcher.BeginInvoke(new Action(() => ActivateMainWindow(options))));

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
		_host.Services.GetRequiredService<PreviewFileStore>().Clean(); // files of earlier previews

		// The WebView2 environment takes a second or more to create; start it now so it overlaps with the rest of the startup.
		_ = _host.Services.GetRequiredService<WebViewEnvironmentProvider>().GetAsync();

		await _host.StartAsync();
		await _host.Services.GetRequiredService<IClientStateStore>().LoadAsync();
		var stateLoaded = startup.ElapsedMilliseconds;

		_host.Services.GetRequiredService<ShellModuleLoader>().Load();
		var window = _host.Services.GetRequiredService<MainWindow>();
		var windowCreated = startup.ElapsedMilliseconds;
		MainWindow = window;
		window.Show();
		if (_activationPending)
			ActivateMainWindow(options); // a second start arrived while this one was still starting
		logger.LogInformation("Startup: host built {Host} ms, state loaded {State} ms, window created {Created} ms, shown {Shown} ms",
			hostBuilt, stateLoaded, windowCreated, startup.ElapsedMilliseconds);
	}

	protected override void OnExit(ExitEventArgs e)
	{
		if (_host != null)
		{
			// A dialog still waiting for an answer would hold its caller forever.
			_host.Services.GetService<IDialogService>()?.CancelPending();

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
		_singleInstance?.Dispose();
		base.OnExit(e);
	}

	/// <summary>
	/// A second start asked for this instance: show, restore and raise the window. A test window must never take focus, so it ignores the request.
	/// A request that comes before the window exists is remembered and applied once it is shown.
	/// </summary>
	private void ActivateMainWindow(StartupOptions options)
	{
		if (options.TestWindow)
			return;

		if (MainWindow is not { IsLoaded: true } window)
		{
			_activationPending = true;
			return;
		}

		_activationPending = false;
		if (window.Visibility != Visibility.Visible)
			window.Show();

		if (window.WindowState == WindowState.Minimized)
			window.WindowState = WindowState.Normal;

		// Activate alone often only flashes the taskbar button; toggling Topmost reliably raises the window.
		window.Activate();
		window.Topmost = true;
		window.Topmost = false;
		window.Focus();
	}
}
