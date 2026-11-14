using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF;

/// <summary>The DI composition root.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ServiceRegistration
{
	public void Register(IServiceCollection services, AppPaths paths, Dispatcher dispatcher)
	{
		services.AddSingleton(paths);
		services.AddSingleton<IUiDispatcher>(new WpfUiDispatcher(dispatcher));
		services.AddTransient<GlobalExceptionLogger>();

		// Daemon connection. Stage B replaces PendingTessynClientFactory with the factory of the real TessynClient.
		services.AddSingleton<IDaemonLocator, DaemonLocator>();
		services.AddSingleton<ITessynClientFactory, PendingTessynClientFactory>();
		services.AddSingleton<ConnectionViewModel>();

		services.AddSingleton<Func<ITessynClient, TreeViewModel>>(sp => client => new TreeViewModel(
			client, sp.GetRequiredService<IUiDispatcher>(), sp.GetRequiredService<ILogger<TreeViewModel>>()));
		services.AddSingleton<MainViewModel>();
		services.AddSingleton<MainWindow>();
	}
}
