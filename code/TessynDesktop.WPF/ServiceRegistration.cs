using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using TessynDesktop.WPF.Infrastructure;
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
		services.AddTransient<MainWindow>();
	}
}
