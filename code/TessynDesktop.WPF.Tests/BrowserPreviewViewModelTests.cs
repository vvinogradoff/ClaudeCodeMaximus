using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>FR.27.4: the browser bar's view model over a fake surface.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class BrowserPreviewViewModelTests
{
	private readonly FakeBrowserSurface _surface = new();
	private readonly FakeUrlLauncher _launcher = new();

	private BrowserPreviewViewModel Create() => new(_surface, new UrlPolicy(), _launcher, new ImmediateUiDispatcher(), NullLogger<BrowserPreviewViewModel>.Instance);

	[Fact]
	public void Navigate_Http_GoesToTheSurface()
	{
		var vm = Create();
		_surface.Attach();
		vm.Navigate("https://example.com/a");

		Assert.Equal(["https://example.com/a"], _surface.Navigated);
		Assert.Equal("https://example.com/a", vm.Address);
		Assert.Null(vm.Error);
	}

	[Theory]
	[InlineData("slack://open")]
	[InlineData("javascript:alert(1)")]
	[InlineData("file:///C:/x.txt")]
	[InlineData("")]
	public void Navigate_NonHttp_SetsErrorAndNavigatesNowhere(string url)
	{
		var vm = Create();
		_surface.Attach();
		vm.Navigate(url);

		Assert.Empty(_surface.Navigated);
		Assert.Null(_surface.PendingUrl);
		Assert.True(vm.HasError);
		Assert.Equal(string.Empty, vm.Address);
	}

	[Fact]
	public void Navigate_BeforeAttach_KeepsOnlyTheNewest()
	{
		var vm = Create();
		vm.Navigate("https://one.example/");
		vm.Navigate("https://two.example/");
		_surface.Attach();

		Assert.Equal(["https://two.example/"], _surface.Navigated);
	}

	[Fact]
	public void BackAndForward_FollowStateChanged()
	{
		var vm = Create();
		Assert.False(vm.BackCommand.CanExecute(null));
		Assert.False(vm.ForwardCommand.CanExecute(null));

		_surface.CanGoBack = true;
		_surface.RaiseState(false);
		Assert.True(vm.BackCommand.CanExecute(null));
		Assert.False(vm.ForwardCommand.CanExecute(null));

		_surface.CanGoForward = true;
		_surface.RaiseState(false);
		vm.BackCommand.Execute(null);
		vm.ForwardCommand.Execute(null);
		Assert.Equal(["back", "forward"], _surface.Calls);

		_surface.CanGoBack = false;
		_surface.RaiseState(false);
		Assert.False(vm.BackCommand.CanExecute(null));
	}

	[Fact]
	public void ReloadOrStop_ReloadsWhenIdle_StopsWhileLoading()
	{
		var vm = Create();
		vm.ReloadOrStopCommand.Execute(null);
		_surface.RaiseState(true);
		Assert.True(vm.IsLoading);
		vm.ReloadOrStopCommand.Execute(null);
		_surface.RaiseState(false);
		Assert.False(vm.IsLoading);
		vm.ReloadOrStopCommand.Execute(null);

		Assert.Equal(["reload", "stop", "reload"], _surface.Calls);
	}

	[Fact]
	public void Title_FollowsTheDocument_AndFallsBackToTheHost()
	{
		var vm = Create();
		var raised = 0;
		vm.TitleChanged += (_, _) => raised++;
		vm.Navigate("https://github.com/tryhardstop/windows-app/pull/1");
		Assert.Equal("github.com", vm.Title);

		_surface.Source = "https://github.com/tryhardstop/windows-app/pull/1";
		_surface.DocumentTitle = "Pull request 1";
		_surface.RaiseState(false);
		Assert.Equal("Pull request 1", vm.Title);

		_surface.DocumentTitle = "";
		_surface.RaiseState(false);
		Assert.Equal("github.com", vm.Title);
		Assert.Equal(3, raised);
	}

	[Fact]
	public void Address_FollowsTheSurfaceSource_IgnoringBlank()
	{
		var vm = Create();
		_surface.Source = "https://example.com/after-redirect";
		_surface.RaiseState(false);
		Assert.Equal("https://example.com/after-redirect", vm.Address);

		_surface.Source = "about:blank";
		_surface.RaiseState(false);
		Assert.Equal("https://example.com/after-redirect", vm.Address);
	}

	[Fact]
	public void OpenInSystemBrowser_SendsTheCurrentAddressOnly()
	{
		var vm = Create();
		Assert.False(vm.OpenInSystemBrowserCommand.CanExecute(null));

		vm.Navigate("https://example.com/page");
		_surface.Source = "https://example.com/final";
		_surface.RaiseState(false);
		Assert.True(vm.OpenInSystemBrowserCommand.CanExecute(null));
		vm.OpenInSystemBrowserCommand.Execute(null);

		Assert.Equal(["https://example.com/final"], _launcher.Opened);
		Assert.Empty(_surface.Navigated);
	}

	[Fact]
	public void Error_ComesFromFailureAndLoss_AndIsDismissible()
	{
		var vm = Create();
		_surface.RaiseFailed("ConnectionAborted");
		Assert.True(vm.HasError);
		Assert.Contains("ConnectionAborted", vm.Error);

		vm.DismissErrorCommand.Execute(null);
		Assert.False(vm.HasError);
		Assert.Null(vm.Error);

		_surface.RaiseLost();
		Assert.Equal(Constants.Browser.BrowserLostMessage, vm.Error);
	}

	[Fact]
	public void Dispose_UnsubscribesFromTheSurface()
	{
		var vm = Create();
		Assert.Equal(1, _surface.StateSubscribers);
		Assert.Equal(1, _surface.FailedSubscribers);
		Assert.Equal(1, _surface.LostSubscribers);

		vm.Dispose();
		Assert.Equal(0, _surface.StateSubscribers);
		Assert.Equal(0, _surface.FailedSubscribers);
		Assert.Equal(0, _surface.LostSubscribers);

		_surface.Source = "https://example.com/";
		_surface.RaiseState(true);
		Assert.False(vm.IsLoading);
	}
}
