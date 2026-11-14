using Microsoft.Web.WebView2.Core;
using TessynDesktop.WPF.Infrastructure;

namespace TessynDesktop.WPF.Views;

/// <summary>The one shared <see cref="CoreWebView2Environment"/> (user data under <c>%AppData%\TessynDesktop\webview2\</c>), created on first use.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class WebViewEnvironmentProvider
{
	private readonly AppPaths _paths;
	private Task<CoreWebView2Environment>? _environment;

	public WebViewEnvironmentProvider(AppPaths paths)
	{
		_paths = paths;
	}

	/// <summary>The shared environment; must be first requested on the UI thread.</summary>
	public Task<CoreWebView2Environment> GetAsync() => _environment ??= CreateAsync();

	private async Task<CoreWebView2Environment> CreateAsync()
	{
		Directory.CreateDirectory(_paths.WebView2UserDataDirectory);
		return await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: _paths.WebView2UserDataDirectory);
	}
}
