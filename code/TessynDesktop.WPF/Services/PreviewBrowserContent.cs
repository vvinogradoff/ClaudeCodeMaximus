using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Services;

/// <summary>The browser content of a web preview window: the browser bar's view model and the surface that drives its WebView2; the window disposes both.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record PreviewBrowserContent(BrowserPreviewViewModel ViewModel, IBrowserSurface Surface) : IDisposable
{
	public void Dispose()
	{
		ViewModel.Dispose();
		(Surface as IDisposable)?.Dispose();
	}
}
