using Microsoft.Web.WebView2.Core;
using TessynDesktop.WPF.Infrastructure;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// The one <see cref="CoreWebView2Environment"/> of the embedded browser (FR.27.1), user data under <c>%AppData%\TessynDesktop\browser\</c>.
/// Separate from <see cref="WebViewEnvironmentProvider"/>, so the in-private transcript and preview pages never see the signed-in profile.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class BrowserEnvironmentProvider
{
	private readonly AppPaths _paths;
	private readonly StartupOptions _options;
	private Task<CoreWebView2Environment>? _environment;

	public BrowserEnvironmentProvider(AppPaths paths, StartupOptions options)
	{
		_paths = paths;
		_options = options;
	}

	/// <summary>The shared environment; must be first requested on the UI thread.</summary>
	public Task<CoreWebView2Environment> GetAsync() => _environment == null || _environment.IsFaulted ? _environment = CreateAsync() : _environment;

	private async Task<CoreWebView2Environment> CreateAsync()
	{
		Directory.CreateDirectory(_paths.BrowserUserDataDirectory);
		// --test-window runs only: no GPU in the browser process (as WebViewEnvironmentProvider, ISSUE 489).
		var environmentOptions = _options.TestWindow ? new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = "--disable-gpu" } : null;
		return await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: _paths.BrowserUserDataDirectory, options: environmentOptions);
	}
}
