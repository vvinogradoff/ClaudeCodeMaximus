using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF;

/// <summary>Application entry: single-instance mutex, Generic Host with DI, Serilog and global exception logging.</summary>
/// <remarks>Created by Claude</remarks>
public partial class App : Application
{
	private IHost? _host;
	private Mutex? _singleInstanceMutex;

	/// <summary>The application's service provider (set once the host is built).</summary>
	public IServiceProvider Services => _host!.Services;

	protected override async void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);

		_singleInstanceMutex = new Mutex(initiallyOwned: true, Constants.Identity.SingleInstanceMutexName, out var createdNew);
		if (!createdNew)
		{
			_singleInstanceMutex.Dispose();
			_singleInstanceMutex = null;
			Shutdown();
			return;
		}

		var paths = new AppPaths();
		Log.Logger = new LoggingSetup().CreateLogger(paths);

		_host = Host.CreateDefaultBuilder()
			.UseSerilog(Log.Logger)
			.ConfigureServices(services => new ServiceRegistration().Register(services, paths, Dispatcher))
			.Build();

		_host.Services.GetRequiredService<GlobalExceptionLogger>().Attach(this);
		_host.Services.GetRequiredService<ILogger<App>>().LogInformation("TessynDesktop.WPF starting");

		await _host.StartAsync();
		await _host.Services.GetRequiredService<IClientStateStore>().LoadAsync();

		var window = _host.Services.GetRequiredService<MainWindow>();
		MainWindow = window;
		window.Show();
	}

	protected override void OnExit(ExitEventArgs e)
	{
		if (_host != null)
		{
			_host.Services.GetRequiredService<IClientStateStore>().FlushAsync().GetAwaiter().GetResult();
			_host.StopAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
			_host.Dispose();
		}

		Log.CloseAndFlush();
		_singleInstanceMutex?.ReleaseMutex();
		_singleInstanceMutex?.Dispose();
		base.OnExit(e);
	}
}
