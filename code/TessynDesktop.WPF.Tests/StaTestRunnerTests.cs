using System.Windows;
using AvalonDock;
using TessynDesktop.WPF.Tests.Fakes;

namespace TessynDesktop.WPF.Tests;

/// <summary>The STA runner's guards: an Application made by an earlier test thread must not hang a later DockingManager, and a hung run fails instead of blocking.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class StaTestRunnerTests
{
	private static readonly TimeSpan Guard = TimeSpan.FromSeconds(30);

	/// <summary>The order that hung the run: a test thread asks for the Application and ends; a later thread loads the first DockingManager of the process.</summary>
	[Fact]
	public void ADockingManager_Loads_AfterAnEarlierTestThreadAskedForTheApplicationAndEnded()
	{
		StaTestRunner.Run(() => _ = StaTestRunner.EnsureApplication());

		var loaded = false;
		StaTestRunner.Run(() =>
		{
			var manager = new DockingManager();
			manager.Loaded += (_, _) => loaded = true;
			var window = new Window { Content = manager, Width = 300, Height = 200, Left = -3000, Top = -3000, ShowActivated = false, ShowInTaskbar = false };
			window.Show();
			window.Close();
		}, Guard);

		Assert.True(loaded);
	}

	[Fact]
	public void TheApplication_IsOneForTheProcess_AndItsDispatcherPumps()
	{
		var application = StaTestRunner.EnsureApplication();

		Assert.Same(application, StaTestRunner.EnsureApplication());
		Assert.Same(application, Application.Current);
		var ran = false;
		application.Dispatcher.Invoke(() => ran = true);
		Assert.True(ran);
	}

	[Fact]
	public void ARunThatDoesNotFinish_FailsNamingTheHang()
	{
		using var release = new ManualResetEventSlim();
		var ex = Assert.Throws<TimeoutException>(() => StaTestRunner.Run(() => release.Wait(), TimeSpan.FromMilliseconds(300)));
		release.Set();
		Assert.Contains("hung", ex.Message);
	}
}
