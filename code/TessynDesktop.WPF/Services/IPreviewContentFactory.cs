using TessynDesktop.Client;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Services;

/// <summary>Builds what a preview window shows (FR.29.5): every window gets its own renderer and its own browser, so windows never disturb each other or the preview pane.</summary>
/// <remarks>Created by Claude</remarks>
public interface IPreviewContentFactory
{
	/// <summary>A file renderer with its own surface for <paramref name="client"/>; disposing the view model disposes the surface. Links inside the preview route (FR.29.4).</summary>
	FilePreviewViewModel CreateFile(ITessynClient client);

	/// <summary>A browser with its own surface; links never route (FR.27.4).</summary>
	PreviewBrowserContent CreateBrowser();
}
