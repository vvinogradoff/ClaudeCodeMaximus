namespace TessynDesktop.WPF.Services;

/// <summary>What <c>BrowserPreviewViewModel</c> drives: the embedded browser of a web preview window (FR.27). Contains no WPF types.</summary>
/// <remarks>Created by Claude</remarks>
public interface IBrowserSurface
{
	/// <summary>Raised when the address, title, history or loading state changed (on any thread).</summary>
	event EventHandler<BrowserStateChangedEventArgs>? StateChanged;

	/// <summary>Raised with a short status text when a page could not be loaded.</summary>
	event EventHandler<string>? NavigationFailed;

	/// <summary>Raised when the browser process exited; the surface is unusable until it is attached again.</summary>
	event EventHandler? BrowserLost;

	bool CanGoBack { get; }

	bool CanGoForward { get; }

	/// <summary>The current top-level address; null before the first navigation.</summary>
	string? Source { get; }

	string? DocumentTitle { get; }

	/// <summary>Navigates to <paramref name="url"/>; before the browser is attached the call is queued and only the newest URL counts.</summary>
	void Navigate(string url);

	void GoBack();

	void GoForward();

	void Reload();

	void Stop();
}
