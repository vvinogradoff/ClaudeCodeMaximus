using Microsoft.Web.WebView2.Core;
using TessynDesktop.WPF.Infrastructure;

namespace TessynDesktop.WPF.Views;

/// <summary>The one shared <see cref="CoreWebView2Environment"/> (user data under <c>%AppData%\TessynDesktop\webview2\</c>), created on first use.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class WebViewEnvironmentProvider
{
	private readonly AppPaths _paths;
	private Task<CoreWebView2Environment>? _environment;

	private readonly StartupOptions _options;

	public WebViewEnvironmentProvider(AppPaths paths, StartupOptions options)
	{
		_paths = paths;
		_options = options;
	}

	/// <summary>The shared environment; must be first requested on the UI thread.</summary>
	public Task<CoreWebView2Environment> GetAsync() => _environment ??= CreateAsync();

	private async Task<CoreWebView2Environment> CreateAsync()
	{
		Directory.CreateDirectory(_paths.WebView2UserDataDirectory);
		// --test-window runs only: no GPU in the browser process (a bugcheck in the AMD display driver hit during a full E2E run, ISSUE 489).
		var environmentOptions = _options.TestWindow ? new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = "--disable-gpu" } : null;
		return await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: _paths.WebView2UserDataDirectory, options: environmentOptions);
	}
}
