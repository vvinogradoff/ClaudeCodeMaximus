using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// <see cref="IBrowserDataCleaner"/>: clears every kind of data of the browser profile (cookies, storage, cache, history, passwords) through
/// <c>CoreWebView2Profile.ClearBrowsingDataAsync</c>. It needs a live browser, so it opens a short-lived hidden window (never shown, never
/// activated) with a WebView2 in the browser environment and closes it afterwards. Call it on the UI thread.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class BrowserDataCleaner : IBrowserDataCleaner
{
	private readonly BrowserEnvironmentProvider _environment;
	private readonly ILogger<BrowserDataCleaner> _logger;

	public BrowserDataCleaner(BrowserEnvironmentProvider environment, ILogger<BrowserDataCleaner> logger)
	{
		_environment = environment;
		_logger = logger;
	}

	public async Task<bool> ClearAsync()
	{
		Window? window = null;
		WebView2? control = null;
		try
		{
			var env = await _environment.GetAsync();
			control = new WebView2();
			window = new Window
			{
				Width = Constants.Browser.CleanerWindowSize,
				Height = Constants.Browser.CleanerWindowSize,
				Left = Constants.Browser.TestWindowOffset,
				Top = Constants.Browser.TestWindowOffset,
				ShowInTaskbar = false,
				ShowActivated = false,
				WindowStyle = WindowStyle.None,
				Content = control,
			};
			window.Show();
			var options = env.CreateCoreWebView2ControllerOptions();
			options.IsInPrivateModeEnabled = false;
			await control.EnsureCoreWebView2Async(env, options);
			await control.CoreWebView2.Profile.ClearBrowsingDataAsync();
			return true;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Clearing the browser data failed");
			return false;
		}
		finally
		{
			control?.Dispose();
			window?.Close();
		}
	}
}
