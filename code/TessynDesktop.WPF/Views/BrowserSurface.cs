using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// <see cref="IBrowserSurface"/> over one WebView2 control (FR.27): a persistent, signed-in profile (<see cref="BrowserEnvironmentProvider"/>),
/// http/https navigation only (<see cref="BrowserNavigationPolicy"/>), no host objects, no web messages, no DevTools, permissions denied
/// without a prompt, <c>LaunchingExternalUriScheme</c> always cancelled, popups in a window of the same profile. Downloads use WebView2's own UI.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class BrowserSurface : IBrowserSurface, IDisposable
{
	private readonly BrowserEnvironmentProvider _environment;
	private readonly BrowserNavigationPolicy _policy;
	private readonly StartupOptions _options;
	private readonly ILogger<BrowserSurface> _logger;
	private readonly List<BrowserPopupWindow> _popups = [];
	private readonly BrowserPopupPolicy _popupPolicy = new();

	private WebView2? _control;
	private CoreWebView2Environment? _env;
	private string? _pending;
	private bool _isLoading;
	private bool _disposed;

	public BrowserSurface(BrowserEnvironmentProvider environment, BrowserNavigationPolicy policy, StartupOptions options, ILogger<BrowserSurface> logger)
	{
		_environment = environment;
		_policy = policy;
		_options = options;
		_logger = logger;
	}

	public event EventHandler<BrowserStateChangedEventArgs>? StateChanged;

	public event EventHandler<string>? NavigationFailed;

	public event EventHandler? BrowserLost;

	public bool CanGoBack => _control?.CoreWebView2?.CanGoBack ?? false;

	public bool CanGoForward => _control?.CoreWebView2?.CanGoForward ?? false;

	public string? Source => _control?.CoreWebView2?.Source;

	public string? DocumentTitle => _control?.CoreWebView2?.DocumentTitle;

	/// <summary>Binds the surface to <paramref name="control"/> and creates its browser in the persistent profile; a queued URL is then navigated.</summary>
	public async Task AttachAsync(WebView2 control)
	{
		_control = control;
		_env = await _environment.GetAsync();
		var controllerOptions = _env.CreateCoreWebView2ControllerOptions();
		controllerOptions.IsInPrivateModeEnabled = false;   // FR.27.1: sign-ins persist
		await control.EnsureCoreWebView2Async(_env, controllerOptions);
		if (_disposed)
			return;

		var core = control.CoreWebView2;
		Configure(core, Window.GetWindow(control), isMain: true);
		var pending = _pending;
		_pending = null;
		if (pending != null)
			core.Navigate(pending);
	}

	public void Navigate(string url)
	{
		var core = _control?.CoreWebView2;
		if (core == null)
		{
			_pending = url; // only the newest URL matters
			return;
		}

		core.Navigate(url);
	}

	public void GoBack() => _control?.CoreWebView2?.GoBack();

	public void GoForward() => _control?.CoreWebView2?.GoForward();

	public void Reload() => _control?.CoreWebView2?.Reload();

	public void Stop() => _control?.CoreWebView2?.Stop();

	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;
		foreach (var popup in _popups.ToList())
			popup.Close();

		_popups.Clear();
		_control?.Dispose();
	}

	private void Configure(CoreWebView2 core, Window? owner, bool isMain)
	{
		var settings = core.Settings;
		settings.AreHostObjectsAllowed = false;
		settings.IsWebMessageEnabled = false;
		settings.AreDevToolsEnabled = false;
		settings.IsStatusBarEnabled = false;
		settings.IsPasswordAutosaveEnabled = false;

		core.NavigationStarting += (_, e) => OnNavigationStarting(e, isMain);
		core.LaunchingExternalUriScheme += OnLaunchingExternalUriScheme;
		core.PermissionRequested += (_, e) =>
		{
			e.Handled = true;
			e.State = CoreWebView2PermissionState.Deny;
		};
		core.NewWindowRequested += (_, e) => OnNewWindowRequested(e, owner);
		core.ProcessFailed += OnProcessFailed;
		if (!isMain)
			return;

		core.SourceChanged += (_, _) => RaiseStateChanged();
		core.HistoryChanged += (_, _) => RaiseStateChanged();
		core.DocumentTitleChanged += (_, _) => RaiseStateChanged();
		core.NavigationCompleted += OnNavigationCompleted;
	}

	private void OnNavigationStarting(CoreWebView2NavigationStartingEventArgs e, bool isMain)
	{
		if (!_policy.IsAllowedTopLevel(e.Uri))
		{
			e.Cancel = true;
			_logger.LogInformation("Blocked a browser navigation to scheme {Scheme}", SchemeOf(e.Uri));
			return;
		}

		if (!isMain)
			return;

		_isLoading = true;
		RaiseStateChanged();
	}

	private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
	{
		_isLoading = false;
		RaiseStateChanged();
		if (e.IsSuccess || e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled)
			return;

		NavigationFailed?.Invoke(this, e.WebErrorStatus.ToString());
	}

	private void OnLaunchingExternalUriScheme(object? sender, CoreWebView2LaunchingExternalUriSchemeEventArgs e)
	{
		e.Cancel = true;
		_logger.LogInformation("Blocked launching an external app for scheme {Scheme}", SchemeOf(e.Uri));
	}

	private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
	{
		_logger.LogWarning("Browser process failed: {Kind}", e.ProcessFailedKind);
		switch (e.ProcessFailedKind)
		{
			case CoreWebView2ProcessFailedKind.BrowserProcessExited:
				BrowserLost?.Invoke(this, EventArgs.Empty);
				break;
			case CoreWebView2ProcessFailedKind.RenderProcessExited:
			case CoreWebView2ProcessFailedKind.RenderProcessUnresponsive:
				(sender as CoreWebView2)?.Reload();
				break;
		}
	}

	private void OnNewWindowRequested(CoreWebView2NewWindowRequestedEventArgs e, Window? owner)
	{
		if (_env == null || _disposed)
			return;

		e.Handled = true;
		var decision = _popupPolicy.Decide(_popups.Count, e.IsUserInitiated);
		if (decision == BrowserPopupDecision.Drop)
		{
			_logger.LogInformation("A browser popup was dropped: {Count} popups are already open", _popups.Count);
			return;
		}

		var deferral = e.GetDeferral();
		_ = OpenPopupAsync(e, deferral, owner, decision == BrowserPopupDecision.ShowActive);
	}

	private async Task OpenPopupAsync(CoreWebView2NewWindowRequestedEventArgs e, CoreWebView2Deferral deferral, Window? owner, bool activate)
	{
		try
		{
			var features = e.WindowFeatures;
			var width = features.HasSize ? features.Width : Constants.Browser.PopupDefaultWidth;
			var height = features.HasSize ? features.Height : Constants.Browser.PopupDefaultHeight;
			var popup = new BrowserPopupWindow(owner, width, height, activate, _options.TestWindow);
			_popups.Add(popup);
			popup.Closed += (_, _) => _popups.Remove(popup);
			var core = await popup.InitializeAsync(_env!);
			Configure(core, popup, isMain: false);
			e.NewWindow = core;
			popup.Show();
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "A browser popup could not be opened");
		}
		finally
		{
			deferral.Complete();
		}
	}

	private void RaiseStateChanged() => StateChanged?.Invoke(this, new BrowserStateChangedEventArgs(_isLoading));

	private string SchemeOf(string? uri) => Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ? parsed.Scheme : "(none)";
}
