using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.ViewModels.Settings;
using TessynDesktop.Client;
using TessynDesktop.WPF.Tests.Fakes;
using Xunit;

namespace TessynDesktop.WPF.Tests;

/// <summary>The real container resolves the preview-window graph (FR.29, FR.6.10).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewContainerTests
{
	[Fact]
	public void UnderTestMonitors_TheRouterTheRoutingLauncherTheTitleBarAndSettingsResolve()
	{
		var root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-previews-" + Guid.NewGuid().ToString("N"));
		try
		{
			StaTestRunner.Run(() =>
			{
				var services = new ServiceCollection();
				services.AddLogging();
				new ServiceRegistration().Register(services, new AppPaths(root), Dispatcher.CurrentDispatcher, StartupOptions.Parse(["--test-window", "--test-monitors", "2"]));
				var provider = services.BuildServiceProvider();
				try
				{
					var router = provider.GetRequiredService<IPreviewRouter>();

					Assert.Same(router, provider.GetRequiredService<IPreviewRouter>());
					Assert.IsType<PreviewRoutingUrlLauncher>(provider.GetRequiredService<IUrlLauncher>());
					Assert.IsType<SystemUrlLauncher>(provider.GetRequiredService<SystemUrlLauncher>());
					Assert.IsType<ScriptedMonitorWatcher>(provider.GetRequiredService<IMonitorWatcher>());
					Assert.NotNull(provider.GetRequiredService<TitleBarViewModel>());
					var settings = provider.GetRequiredService<Func<ITessynClient, SettingsViewModel>>();
					Assert.NotNull(settings);
					Assert.NotNull(provider.GetRequiredService<IPreviewContentFactory>());
					Assert.NotNull(provider.GetRequiredService<IPreviewWindowFactory>());
					Assert.NotNull(provider.GetRequiredService<IBrowserDataCleaner>());
					Assert.NotNull(provider.GetRequiredService<ITiffConverter>());
				}
				finally
				{
					provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
				}
			});
		}
		finally
		{
			TestDirectory.Delete(root);
		}
	}
}
